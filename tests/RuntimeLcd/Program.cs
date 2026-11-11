using System;

static class Program
{
    static void Main()
    {
        int checks = 0;
        ControllerTests.Run((passed, description) =>
        {
            if (!passed) throw new InvalidOperationException(description);
            checks++;
        });
        Console.WriteLine("Passed " + checks + " Runtime LCD checks.");
    }
}
