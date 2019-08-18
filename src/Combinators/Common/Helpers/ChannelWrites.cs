using System;
using System.Threading.Tasks;

namespace Helpers
{
    using System.Threading.Channels;

    public class ChannelsQueue
    {
        private ChannelWriter<string> _writer;

        public ChannelsQueue()
        {
            var channel = Channel.CreateUnbounded<string>();
            var reader = channel.Reader;
            _writer = channel.Writer;

            Task.Factory.StartNew(async () =>
            {
                // Wait while channel is not empty and still not completed
                while (await reader.WaitToReadAsync())
                {
                    var job = await reader.ReadAsync();
                    Console.WriteLine(job);
                }
            }, TaskCreationOptions.LongRunning);
        }

        public async Task Enqueue(string job)
        {
            await _writer.WriteAsync(job);
        }

        public void Stop()
        {
            _writer.Complete();
        }
    }

    /*
     * As you can see, it’s very straightforward. It reminds me a bit of ConcurrentQueue, but it’s really much more.
     * For one thing, it has a fully asynchronous API. It has blocking functionality with WaitToReadAsync,
     * where it will wait on an empty channel until a job is added to the channel or until writer.Complete() is called.
     * It also has Bound capabilities, where the channel has a limit. When the limit is reached,
     * the WriteAsync task waits until the channel can add the given job. That’s why Write is a Task.
     */
    public class ChannelsQueueMultiThreads
    {
        private ChannelWriter<string> _writer;

        public ChannelsQueueMultiThreads(int threads)
        {
            var channel = Channel.CreateUnbounded<string>();
            var reader = channel.Reader;
            _writer = channel.Writer;
            for (int i = 0; i < threads; i++)
            {
                var threadId = i;
                Task.Factory.StartNew(async () =>
                {
                    // Wait while channel is not empty and still not completed
                    while (await reader.WaitToReadAsync())
                    {
                        var job = await reader.ReadAsync();
                        Console.WriteLine(job);
                    }
                }, TaskCreationOptions.LongRunning);
            }
        }

        public void Enqueue(string job)
        {
            _writer.WriteAsync(job).GetAwaiter().GetResult();
        }

        public void Stop()
        {
            _writer.Complete();
        }
    }
}