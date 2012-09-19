using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace MvcGuestbook_Data
{
    public class GuestbookQueueMessage
    {

        public Uri BlobUri { get; set; }

        public string PartitionKey { get; set; }

        public string RowKey { get; set; }
    }
}