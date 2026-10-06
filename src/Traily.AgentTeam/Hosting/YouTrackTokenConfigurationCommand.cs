using System.Text;
using Traily.AgentTeam.Integrations.YouTrack;

namespace Traily.AgentTeam.Hosting;

public sealed class YouTrackTokenConfigurationCommand(YouTrackConfigurationStore configurations)
{
    public async Task<int> RunAsync(string connectionId, CancellationToken cancellationToken = default)
    {
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("Enter the token in an interactive terminal; do not pass it in command arguments.");
            return 1;
        }
        Console.Write("YouTrack access token (hidden): ");
        var input = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Escape)
            {
                Console.WriteLine();
                return 1;
            }
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (input.Length > 0)
                    input.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
                input.Append(key.KeyChar);
        }
        Console.WriteLine();
        try
        {
            await configurations.SetAccessTokenAsync(connectionId, input.ToString(), cancellationToken);
            Console.WriteLine("The encrypted connection token was saved.");
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"The token could not be saved. Failure type: {exception.GetType().Name}.");
            return 1;
        }
        finally { input.Clear(); }
    }
}
