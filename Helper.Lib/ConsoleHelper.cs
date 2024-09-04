namespace Helper.Lib;

using System;

public class ConsoleHelper
{
    public static void WriteHeader(string headerText)
    {
        Console.WriteLine("----------------------------------------------------------------------");
        Console.WriteLine($"{headerText}");
        Console.WriteLine("----------------------------------------------------------------------\n");
    }

    public static void ConsoleWrite(bool verbose, string message) => Console.WriteLine(verbose ? $"{DateTime.Now} {message}" : $"{message}");
}
