using System;

namespace ConsoleTaskEx
{
    using ImageDetection;
    using System.IO;
    using System.Threading.Tasks;

    class Program
    {

        static void Main(string[] args)
        {
            var images = Directory.GetFiles("../../Data/Images", "*.jpg");
            var destination = "./Images/Output";
            if (!Directory.Exists(destination))
                Directory.CreateDirectory(destination);

            foreach (var image in images)
            {
                Console.WriteLine($"Processing {Path.GetFileNameWithoutExtension(image)}");

                FaceDetection.DetectFaces(image, destination);
            }

            Console.WriteLine("Completed");
            Console.ReadLine();

            Console.WriteLine("Hello World!");
        }
    }
}
