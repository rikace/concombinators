using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


namespace DataParallelism.Pipelines
{
    //public class PipelineWorker<TInput, TOutput>
    //{
    //    Func<TInput, TOutput> _processor = null;
    //    Action<TInput> _outputProcessor = null;
    //    BlockingCollection<TInput>[] _input;
    //    CancellationToken _token;

    //    public const int Count = 10;
    //    public PipelineWorker(
    //            BlockingCollection<TInput>[] input,
    //            Func<TInput, TOutput> processor,
    //            CancellationToken token,
    //            string name)
    //    {
    //        _input = input;
    //        Output = new BlockingCollection<TOutput>[_input.Length];
    //        for (int i = 0; i < Output.Length; i++)
    //            Output[i] = null == input[i] ? null : new BlockingCollection<TOutput>(Count);

    //        _processor = processor;
    //        _token = token;
    //        Name = name;
    //    }

    //    public PipelineWorker(
    //            BlockingCollection<TInput>[] input,
    //            Action<TInput> renderer,
    //            CancellationToken token,
    //            string name)
    //    {
    //        _input = input;
    //        _outputProcessor = renderer;
    //        _token = token;
    //        Name = name;
    //        Output = null;
    //    }

    //    public BlockingCollection<TOutput>[] Output { get;  set; }

    //    public string Name { get;  set; }

    //    public void Run()
    //    {
    //        Console.WriteLine("{0} is running", this.Name);
    //        while (!_input.All(bc => bc.IsCompleted) && !_token.IsCancellationRequested)
    //        {
    //            TInput receivedItem;
    //            int i = BlockingCollection<TInput>.TryTakeFromAny(
    //                    _input, out receivedItem, 50, _token);
    //            if (i >= 0)
    //            {
    //                if (Output != null)
    //                {
    //                    TOutput outputItem = _processor(receivedItem);
    //                    BlockingCollection<TOutput>.AddToAny(Output, outputItem);
    //                    Console.WriteLine("{0} sent {1} to next, on thread id {2}",
    //                        Name, outputItem, Thread.CurrentThread.ManagedThreadId);
    //                    Thread.Sleep(TimeSpan.FromMilliseconds(100));
    //                }
    //                else
    //                {
    //                    _outputProcessor(receivedItem);
    //                }
    //            }
    //            else
    //            {
    //                Thread.Sleep(TimeSpan.FromMilliseconds(50));
    //            }
    //        }
    //        if (Output != null)
    //        {
    //            foreach (var bc in Output) bc.CompleteAdding();
    //        }
    //    }
    //}
}



