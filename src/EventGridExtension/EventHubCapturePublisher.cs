﻿using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace Microsoft.Azure.WebJobs.Extensions.EventGrid
{
    public class EventHubCapturePublisher : IPublisher
    {
        public const string Name = "eventHubCapture";
        private List<IDisposable> _recycles = null;

        public string PublisherName
        {
            get { return Name; }
        }

        public List<IDisposable> Recycles
        {
            get { return _recycles; }
        }

        public Dictionary<string, Type> ExtractBindingContract(Type t)
        {
            var contract = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            // TODO we can determine the ACTION in this function, so that when calling ExtractBindingData, we don't have to do the comparison again
            if (t == typeof(EventGridEvent))
            {
                contract.Add("EventGridTrigger", t);
            }
            else if (t == typeof(Stream) || t == typeof(string) || t == typeof(CloudBlob) || t == typeof(byte[]))
            {
                contract.Add("EventGridTrigger", t);
                contract.Add("BlobTrigger", typeof(string));
                contract.Add("Uri", typeof(Uri));
                contract.Add("Properties", typeof(BlobProperties));
                contract.Add("Metadata", typeof(IDictionary<string, string>));
            }
            else
            {
                // fail
                return null;
            }
            return contract;

        }

        public Dictionary<string, object> ExtractBindingData(EventGridEvent e, Type t)
        {
            var bindingData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (t == typeof(EventGridEvent))
            {
                bindingData.Add("EventGridTrigger", e);
            }
            else
            {
                StorageBlob data = e.Data.ToObject<StorageBlob>();
                var blob = new CloudBlob(data.FileUrl);
                // set metadata based on https://github.com/MicrosoftDocs/azure-docs/blob/master/articles/azure-functions/functions-bindings-storage-blob.md#trigger-metadata
                //BlobTrigger.Type string.The triggering blob path
                bindingData.Add("BlobTrigger", blob.Container.Name + "/" + blob.Name);
                //Uri.Type System.Uri.The blob's URI for the primary location.
                bindingData.Add("Uri", blob.Uri);
                //Properties.Type Microsoft.WindowsAzure.Storage.Blob.BlobProperties.The blob's system properties.
                bindingData.Add("Properties", blob.Properties);
                //Metadata.Type IDictionary<string, string>.The user - defined metadata for the blob
                bindingData.Add("Metadata", blob.Metadata);
                // [Blob("output/copy-{name}")] out string output, does not apply here
                // bindingData.Add("name", blob.Name);
                if (t == typeof(CloudBlob))
                {
                    bindingData.Add("EventGridTrigger", blob);
                }
                else
                {
                    // convert from stream 
                    HttpWebRequest myHttpWebRequest = (HttpWebRequest)WebRequest.Create(data.FileUrl);
                    if (t == typeof(Stream))
                    {
                        _recycles = new List<IDisposable>();
                        // SHUN TODO async
                        HttpWebResponse myHttpWebResponse = (HttpWebResponse)myHttpWebRequest.GetResponse();
                        Stream responseStream = myHttpWebResponse.GetResponseStream();
                        _recycles.Add(responseStream);
                        _recycles.Add(myHttpWebResponse);

                        bindingData.Add("EventGridTrigger", responseStream);
                    }
                    // copy to memory => use case javascript
                    else if (t == typeof(Byte[]))
                    {
                        using (HttpWebResponse myHttpWebResponse = (HttpWebResponse)myHttpWebRequest.GetResponse())
                        {
                            using (Stream netWorkStream = myHttpWebResponse.GetResponseStream())
                            {
                                using (MemoryStream ms = new MemoryStream())
                                {
                                    netWorkStream.CopyTo(ms);
                                    bindingData.Add("EventGridTrigger", ms.ToArray());
                                }
                            }
                        }
                    }
                    else if (t == typeof(string))
                    {
                        using (HttpWebResponse myHttpWebResponse = (HttpWebResponse)myHttpWebRequest.GetResponse())
                        {
                            using (StreamReader responseStream = new StreamReader(myHttpWebResponse.GetResponseStream()))
                            {
                                string blobData = responseStream.ReadToEnd();
                                bindingData.Add("EventGridTrigger", blobData);
                            }
                        }
                    }
                }
            }
            return bindingData;
        }

        public object GetArgument(Dictionary<string, object> bindingData)
        {
            return bindingData["EventGridTrigger"];
        }
    }
}