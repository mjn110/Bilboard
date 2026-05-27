using System;

namespace Bilboard.Application.Interfaces
{
    public interface IConsoleService
    {
        void WriteLineYellow(string message);
        void WriteLineGreen(string message);
        void WriteLineCyan(string message);
    }
}
