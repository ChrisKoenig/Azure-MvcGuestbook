using System;
using System.IO;
using System.Web;
using System.Linq;
using System.Web.Mvc;
using System.Diagnostics;
using MvcGuestbook.Models;
using Microsoft.WindowsAzure;
using System.Collections.Generic;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;

namespace MvcGuestbook.Controllers
{
    public class HomeController : Controller
    {

        public ActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public ActionResult Index(string username, string message, HttpPostedFileBase inputFile)
        {

            // read account configuration settings
            var storageAccount = CloudStorageAccount.FromConfigurationSetting("DataConnectionString");

            // create blob container for images
            var blobStorage = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer blobContainer = blobStorage.GetContainerReference("guestbookpics");
            blobContainer.CreateIfNotExist();

            // configure container for public access
            var blobPermissions = blobContainer.GetPermissions();
            blobPermissions.PublicAccess = BlobContainerPublicAccessType.Container;
            blobContainer.SetPermissions(blobPermissions);

            // create queue to communicate with worker role
            var queueClient = storageAccount.CreateCloudQueueClient();
            var queue = queueClient.GetQueueReference("guestbookthumbs");
            queue.CreateIfNotExist();

            // upload the image to blob storage
            string uniqueBlobName = string.Format("image_{0}{1}", Guid.NewGuid(), Path.GetExtension(inputFile.FileName));
            var blob = blobContainer.GetBlobReference(uniqueBlobName);
            blob.Properties.ContentType = inputFile.ContentType;
            blob.UploadFromStream(inputFile.InputStream);
            Trace.TraceInformation("Uploaded image '{0}' to blob storage as '{1}'", inputFile.FileName, uniqueBlobName);

            // create a new entry in table storage
            var entry = new GuestBookEntry()
            {
                GuestName = username,
                Message = message,
                PhotoUrl = blob.Uri.ToString(),
                ThumbnailUrl = blob.Uri.ToString()
            };

            var ds = new GuestBookDataSource();
            ds.AddGuestBookEntry(entry);
            Trace.TraceInformation("Added entry {0}-{1} in table storage for guest '{2}'", entry.PartitionKey, entry.RowKey, entry.GuestName);

            var queueData = new GuestbookQueueMessage()
            {
                BlobUri = blob.Uri,
                PartitionKey = entry.PartitionKey,
                RowKey = entry.RowKey
            };
            var queueMessage = new CloudQueueMessage(JsonConvert.SerializeObject(queueData));
            queue.AddMessage(queueMessage);
            Trace.TraceInformation("Queued message to process blob '{0}'", uniqueBlobName);

            return RedirectToAction("Index");
        }

        public JsonResult Entries()
        {
            var ds = new GuestBookDataSource();
            var entries = ds.GetGuestBookEntries();
            return Json(entries, JsonRequestBehavior.AllowGet);
        }
    }
}
