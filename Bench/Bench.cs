using System.Drawing;
using BenchmarkDotNet.Running;
using Microsoft.VisualBasic.CompilerServices;

namespace Bench
{
    class Program
    {
        static void Main(string[] args)
        {
            BenchmarkSwitcher
                .FromAssembly(typeof(Program).Assembly)
                .Run(args);
        }
    }
}
