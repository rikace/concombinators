using System;
using System.Collections.Concurrent;
using System.Linq;

namespace DataParallelism.Pipelines
{
    //public class PipelineFilter<TInput, TOutput>
    //{
    //    Func<TInput, TOutput> m_function = null;
    //    public BlockingCollection<TInput>[] m_inputData = null;
    //    public BlockingCollection<TOutput>[] m_outputData = null;
    //    Action<TInput> m_outputAction = null;
    //    public string Name { get; private set; }

    //    public PipelineFilter(BlockingCollection<TInput>[] input, Func<TInput, TOutput> processor, string name)
    //    {
    //        m_inputData = input;
    //        m_outputData = new BlockingCollection<TOutput>[3];
    //        for (int i = 0; i < m_outputData.Length; i++)
    //            m_outputData[i] = new BlockingCollection<TOutput>(100);

    //        m_function = processor;
    //        Name = name;
    //    }

    //    //used for final endpoint
    //    public PipelineFilter(BlockingCollection<TInput>[] input, Action<TInput> renderer, string name)
    //    {
    //        m_inputData = input;
    //        m_outputAction = renderer;
    //        Name = name;
    //    }

    //    public void Run()
    //    {
    //        Console.WriteLine("filter {0} is running", this.Name);
    //        while (!m_inputData.All(bc => bc.IsCompleted))
    //        {
    //            TInput receivedItem;
    //            int i = BlockingCollection<TInput>.TryTakeFromAny(
    //                m_inputData, out receivedItem, 50);
    //            if (i >= 0)
    //            {
    //                if (m_outputData != null)
    //                {
    //                    TOutput outputItem = m_function(receivedItem);
    //                    BlockingCollection<TOutput>.AddToAny(m_outputData, outputItem);
    //                    Console.WriteLine("{0} sent {1} to next filter", this.Name, outputItem);
    //                }
    //                else
    //                {
    //                    m_outputAction(receivedItem);
    //                }
    //            }
    //            else
    //                Console.WriteLine("Could not get data from previous filter");
    //        }

    //        if (m_outputData != null)
    //        {
    //            foreach (var bc in m_outputData) bc.CompleteAdding();
    //        }
    //    }
    //}
}