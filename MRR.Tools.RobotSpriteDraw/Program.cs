// Manual hardware check: connects directly to one AIM robot over its WebSocket API
// (bypassing the game/DB layer entirely -- same spirit as MRR.Tools.FirmwareCompare) and
// draws a sprite on its LCD via a sequence of lcd_draw_rectangle commands.
//
// Usage:
//   dotnet run --project MRR.Tools.RobotSpriteDraw

using MRR.Tools.RobotSpriteDraw;

const string RobotIp = "192.168.1.149";

using CancellationTokenSource shutdown = new();
await using AIMWebSocketClient aim = new(RobotIp);

await aim.ConnectAsync(shutdown.Token);

Console.WriteLine("Press Enter to draw Twonky.");
Console.ReadLine();


await aim.DrawRobotAsync(
    originX: 88,
    originY: 88,
    scale: 1,
    cancellationToken: shutdown.Token
);

Console.ReadLine();
