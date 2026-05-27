using System;
using Bilboard.Application.Interfaces;

namespace Bilboard.Application.Services
{
    public class ConsoleService : IConsoleService
    {
        public void WriteLineYellow(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        public void WriteLineGreen(string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(message);
            Console.WriteLine();
            Console.ResetColor();
        }

        public void WriteLineCyan(string message)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }
}
