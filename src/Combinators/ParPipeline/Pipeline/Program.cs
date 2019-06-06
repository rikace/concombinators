using Helpers;
using System;

namespace Pipeline
{
    class Program
    {
        static void Main(string[] args)
        {
            BenchPerformance.Time("Test", () =>
            {
                int sum = 0;
                for (int i = 0; i < 100; i++)
                {
                    sum += i;
                }      
            }, 10);


            Console.WriteLine("Hello World!");
            Console.ReadLine();
        }
    }
}
