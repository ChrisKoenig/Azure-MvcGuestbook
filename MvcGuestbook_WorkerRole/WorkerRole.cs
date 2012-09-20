using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Drawing;
using MvcGuestbook_Data;
using System.Diagnostics;
using Microsoft.WindowsAzure;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using Microsoft.WindowsAzure.StorageClient;
using Microsoft.WindowsAzure.ServiceRuntime;
using Newtonsoft.Json;

namespace MvcGuestbook_WorkerRole
{
    public class WorkerRole : RoleEntryPoint
    {
        private CloudQueue queue;
        private CloudBlobContainer blobContainer;

        public override void Run()
        {
            Trace.TraceInformation("Listening for queue messages...");

            while (true)
            {
                try
                {
                    // retrieve a new message from the queue
                    CloudQueueMessage msg = this.queue.GetMessage();
                    if (msg != null)
                    {
                        // parse message retrieved from queue
                        var json = msg.AsString;
                        var data = JsonConvert.DeserializeObject<GuestbookQueueMessage>(json);
                        var imageBlobUri = data.BlobUri.ToString();
                        var partitionKey = data.PartitionKey;
                        var rowKey = data.RowKey;
                        Trace.TraceInformation("Processing image in blob '{0}'.", imageBlobUri);

                        string thumbnailBlobUri = System.Text.RegularExpressions.Regex.Replace(imageBlobUri, "([^\\.]+)(\\.[^\\.]+)?$", "$1-thumb$2");

                        CloudBlob inputBlob = this.blobContainer.GetBlobReference(imageBlobUri);
                        CloudBlob outputBlob = this.blobContainer.GetBlobReference(thumbnailBlobUri);

                        using (BlobStream input = inputBlob.OpenRead())
                        using (BlobStream output = outputBlob.OpenWrite())
                        {
                            this.ProcessImage(input, output);

                            // commit the blob and set its properties
                            output.Commit();
                            outputBlob.Properties.ContentType = "image/jpeg";
                            outputBlob.SetProperties();

                            // update the entry in table storage to point to the thumbnail
                            GuestBookService ds = new GuestBookService("DataConnectionString");
                            ds.UpdateImageThumbnail(partitionKey, rowKey, thumbnailBlobUri);

                            // remove message from queue
                            this.queue.DeleteMessage(msg);

                            Trace.TraceInformation("Generated thumbnail in blob '{0}'.", thumbnailBlobUri);
                        }
                    }
                    else
                    {
                        System.Threading.Thread.Sleep(1000);
                    }
                }
                catch (StorageClientException e)
                {
                    Trace.TraceError("Exception when processing queue item. Message: '{0}'", e.Message);
                    System.Threading.Thread.Sleep(5000);
                }
            }
        }

        public override bool OnStart()
        {
            // Set the maximum number of concurrent connections 
            ServicePointManager.DefaultConnectionLimit = 12;

            // read storage account configuration settings
            CloudStorageAccount.SetConfigurationSettingPublisher((configName, configSetter) =>
            {
                configSetter(RoleEnvironment.GetConfigurationSettingValue(configName));
            });
            
            var storageAccount = CloudStorageAccount.FromConfigurationSetting("DataConnectionString");

            // initialize blob storage
            var blobStorage = storageAccount.CreateCloudBlobClient();
            blobContainer = blobStorage.GetContainerReference("guestbookpics");

            // initialize queue storage
            var queueStorage = storageAccount.CreateCloudQueueClient();
            queue = queueStorage.GetQueueReference("guestbookthumbs");

            Trace.TraceInformation("Creating container and queue...");

            bool storageInitialized = false;
            while (!storageInitialized)
            {
                try
                {
                    // create the blob container and allow public access
                    this.blobContainer.CreateIfNotExist();
                    var permissions = this.blobContainer.GetPermissions();
                    permissions.PublicAccess = BlobContainerPublicAccessType.Container;
                    this.blobContainer.SetPermissions(permissions);

                    // create the message queue(s)
                    this.queue.CreateIfNotExist();

                    storageInitialized = true;
                }
                catch (StorageClientException e)
                {
                    if (e.ErrorCode == StorageErrorCode.TransportError)
                    {
                        Trace.TraceError(
                            "Storage services initialization failure. "
                          + "Check your storage account configuration settings. If running locally, "
                          + "ensure that the Development Storage service is running. Message: '{0}'",
                          e.Message);
                        System.Threading.Thread.Sleep(5000);
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            return base.OnStart();
        }

        public void ProcessImage(Stream input, Stream output)
        {
            int width;
            int height;
            var originalImage = new Bitmap(input);

            if (originalImage.Width > originalImage.Height)
            {
                width = 128;
                height = 128 * originalImage.Height / originalImage.Width;
            }
            else
            {
                height = 128;
                width = 128 * originalImage.Width / originalImage.Height;
            }

            Bitmap thumbnailImage = null;

            try
            {
                thumbnailImage = new Bitmap(width, height);

                using (Graphics graphics = Graphics.FromImage(thumbnailImage))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(originalImage, 0, 0, width, height);
                }

                thumbnailImage.Save(output, ImageFormat.Jpeg);
            }
            finally
            {
                if (thumbnailImage != null)
                {
                    thumbnailImage.Dispose();
                }
            }
        }
    }

}
