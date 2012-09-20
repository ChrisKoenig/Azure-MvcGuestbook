using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.WindowsAzure.StorageClient;

namespace MvcGuestbook_Data
{
    public class GuestBookTableServiceContext : TableServiceContext
    {
        public GuestBookTableServiceContext(string baseAddress, Microsoft.WindowsAzure.StorageCredentials credentials)
            : base(baseAddress, credentials)
        {
        }

        public IQueryable<GuestBookEntry> GuestBookEntry
        {
            get
            {
                return this.CreateQuery<GuestBookEntry>("GuestBookEntry");
            }
        }
    }
}