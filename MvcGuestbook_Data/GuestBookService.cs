using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace MvcGuestbook_Data
{
    public class GuestBookService
    {
        private const string PARTITION_KEY_FORMAT_STRING = "MMddyyyy";
        private const string BLOB_CONTAINER_NAME = "guestbookpics";
        private const string QUEUE_NAME = "guestbookthumbs";
        private const string TABLE_NAME = "GuestBookEntry";
        private const string UNIQUE_BLOB_NAME_FORMAT_STRING = "image_{0}{1}";
        private readonly CloudStorageAccount storageAccount;
        private readonly GuestBookTableServiceContext tableServiceContext;

        public GuestBookService(string connectionString)
        {
            storageAccount = CloudStorageAccount.FromConfigurationSetting(connectionString);
            string endpoint = storageAccount.TableEndpoint.AbsoluteUri;
            var credentials = storageAccount.Credentials;
            CloudTableClient.CreateTablesFromModel(typeof(GuestBookTableServiceContext), endpoint, credentials);
            tableServiceContext = new GuestBookTableServiceContext(endpoint, credentials)
            {
                RetryPolicy = RetryPolicies.Retry(3, TimeSpan.FromSeconds(1))
            };
        }

        public IEnumerable<GuestBookEntry> GetGuestBookEntries()
        {
            var results = from g in tableServiceContext.GuestBookEntry
                          where g.PartitionKey == DateTime.UtcNow.ToString(PARTITION_KEY_FORMAT_STRING)
                          select g;
            return results;
        }

        public void UpdateImageThumbnail(string partitionKey, string rowKey, string thumbUrl)
        {
            var results = from g in tableServiceContext.GuestBookEntry
                          where g.PartitionKey == partitionKey && g.RowKey == rowKey
                          select g;

            var entry = results.FirstOrDefault<GuestBookEntry>();
            entry.ThumbnailUrl = thumbUrl;
            tableServiceContext.UpdateObject(entry);
            tableServiceContext.SaveChanges();
        }

        public void AddGuestBookEntry(string username, string message, string filename, string contentType, Stream fileStream)
        {
            // create blob container for images
            var blobStorage = storageAccount.CreateCloudBlobClient();
            CloudBlobContainer blobContainer = blobStorage.GetContainerReference(BLOB_CONTAINER_NAME);
            blobContainer.CreateIfNotExist();

            // configure container for public access
            var blobPermissions = blobContainer.GetPermissions();
            blobPermissions.PublicAccess = BlobContainerPublicAccessType.Container;
            blobContainer.SetPermissions(blobPermissions);

            // create queue to communicate with worker role
            var queueClient = storageAccount.CreateCloudQueueClient();
            var queue = queueClient.GetQueueReference(QUEUE_NAME);
            queue.CreateIfNotExist();

            // upload the image to blob storage
            string uniqueBlobName = string.Format(UNIQUE_BLOB_NAME_FORMAT_STRING, Guid.NewGuid(), Path.GetExtension(filename));
            var blob = blobContainer.GetBlobReference(uniqueBlobName);
            blob.Properties.ContentType = contentType;
            blob.UploadFromStream(fileStream);
            Trace.TraceInformation("Uploaded image '{0}' to blob storage as '{1}'", filename, uniqueBlobName);

            // create a new entry in table storage
            var entry = new GuestBookEntry()
            {
                GuestName = username,
                Message = message,
                PhotoUrl = blob.Uri.ToString(),
                ThumbnailUrl = blob.Uri.ToString()
            };
            tableServiceContext.AddObject(TABLE_NAME, entry);
            tableServiceContext.SaveChanges();
            Trace.TraceInformation("Added entry {0}-{1} in table storage for guest '{2}'", entry.PartitionKey, entry.RowKey, entry.GuestName);

            // drop item into the queue
            var queueData = new GuestbookQueueMessage()
            {
                BlobUri = blob.Uri,
                PartitionKey = entry.PartitionKey,
                RowKey = entry.RowKey
            };
            var queueMessage = new CloudQueueMessage(JsonConvert.SerializeObject(queueData));
            queue.AddMessage(queueMessage);
            Trace.TraceInformation("Queued message to process blob '{0}'", uniqueBlobName);
        }
    }
}