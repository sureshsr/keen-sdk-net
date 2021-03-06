﻿using Keen.Core.EventCache;
using Newtonsoft.Json.Linq;
using PCLStorage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Keen.Core
{
    /// <summary>
    /// <para>EventCachePortable implements the IEventCache interface using
    /// file-based storage via the PCLStorage library. It has no
    /// cache-expiration policy.</para>
    /// <para>To use, pass an instance of this class when constructing KeenClient.
    /// To construct a new instance, call the static New() method.</para>
    /// </summary>
    public class EventCachePortable : IEventCache
    {
        private static Queue<string> events = new Queue<string>();

        private EventCachePortable()
        {}

        /// <summary>
        /// Create, initialize and return an instance of EventCachePortable.
        /// </summary>
        /// <returns></returns>
        public static EventCachePortable New()
        {
            try
            {
                return NewAsync().Result;
            }
            catch (AggregateException ex)
            {
                Debug.WriteLine(ex.TryUnwrap());
                throw ex.TryUnwrap();
            } 
        }

        /// <summary>
        /// Create, initialize and return an instance of EventCachePortable.
        /// </summary>
        /// <returns></returns>
        public static async Task<EventCachePortable> NewAsync()
        {
            var instance = new EventCachePortable();

            var keenFolder = await getKeenFolder()
                .ConfigureAwait(continueOnCapturedContext: false);
            var files = (await keenFolder.GetFilesAsync().ConfigureAwait(continueOnCapturedContext: false)).ToList();

            lock(events)
                if (events.Any())
                    foreach (var f in files)
                        events.Enqueue(f.Name);

            return instance;
        }

        public async Task Add(CachedEvent e)
        {
            if (null == e)
                throw new KeenException("Cached events may not be null");

            var keenFolder = await getKeenFolder()
                .ConfigureAwait(continueOnCapturedContext: false);

            IFile file;
            var attempts = 0;
            var done = false;
            string name = null;
            do
            {
                attempts++;

                // Avoid race conditions in parallel environment by locking on the events queue
                // and generating and inserting a unique name within the lock. CreateFileAsync has
                // a CreateCollisionOption.GenerateUniqueName, but it will return the same name
                // multiple times when called from parallel tasks.
                // If creating and writing the file fails, loop around and 
                if (string.IsNullOrEmpty(name))
                    lock (events)
                    {
                        var i = 0;
                        while (events.Contains(name = e.Collection + i++))
                            ;
                        events.Enqueue(name);
                    }

                Exception lastErr = null;
                try
                {
                    file = await keenFolder.CreateFileAsync(name, CreationCollisionOption.FailIfExists)
                        .ConfigureAwait(continueOnCapturedContext: false);

                    var content = JObject.FromObject(e).ToString();

                    await file.WriteAllTextAsync(content)
                        .ConfigureAwait(continueOnCapturedContext: false);

                    done = true;
                }
                catch (Exception ex)
                {
                    lastErr = ex;
                }

                // If the file was not created, not written, or partially written,
                // the events queue may be left with a file name that references a 
                // file that is nonexistent, empty, or invalid. It's easier to handle
                // this when the queue is read than to try to dequeue the name.
                if (attempts > 100)
                    throw new KeenException("Persistent failure while saving file, aborting", lastErr);
            } while (!done);           
        }

        public async Task<CachedEvent> TryTake()
        {
            var keenFolder = await getKeenFolder()
                .ConfigureAwait(continueOnCapturedContext: false);
            if (!events.Any())
                return null;

            string fileName;
            lock(events)
                fileName = events.Dequeue();

            var file = await keenFolder.GetFileAsync(fileName)
                .ConfigureAwait(continueOnCapturedContext: false);
            var content = await file.ReadAllTextAsync()
                .ConfigureAwait(continueOnCapturedContext: false);
            var ce = JObject.Parse(content);

			var item = new CachedEvent((string)ce.SelectToken("Collection"), (JObject)ce.SelectToken("Event"), ce.SelectToken("Error").ToObject<Exception>() );
            await file.DeleteAsync()
                .ConfigureAwait(continueOnCapturedContext: false);
            return item;
        }

        public async Task Clear()
        {
            var keenFolder = await getKeenFolder()
                .ConfigureAwait(continueOnCapturedContext: false);
            lock(events)
                events.Clear();
            await keenFolder.DeleteAsync()
                .ConfigureAwait(continueOnCapturedContext: false);
            await getKeenFolder()
                .ConfigureAwait(continueOnCapturedContext: false);
        }

        private static async Task<IFolder> getKeenFolder()
        {
            IFolder rootFolder = FileSystem.Current.LocalStorage;
            var keenFolder = await rootFolder.CreateFolderAsync("KeenCache", CreationCollisionOption.OpenIfExists)
                .ConfigureAwait(continueOnCapturedContext: false);
            return keenFolder;
        }

    }
}
