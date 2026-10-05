using System;
using System.Collections.Generic;
using System.Threading;
using Skylines.Bridge;

// Implementation under test for protocol/reference/conformance.py. See that file's docstring for the CLI contract:
//   host  --port P --heartbeat-ms N --timeout-ms N --app NAME
//   guest --port P --app NAME --major N --retry-ms N
// Prints READY, then EVENT lines; echoes app frame 0x0100 as 0x0101; sends GOODBYE(SHUTTING_DOWN) when stdin closes.

internal static class Program
{
    private const ushort EchoRequest = 0x0100, EchoReply = 0x0101;
    private static readonly object OutLock = new object();
    private static volatile bool _stop;

    private static void Print(string line)
    {
        lock (OutLock)
        {
            Console.Out.WriteLine(line);
            Console.Out.Flush();
        }
    }

    private static int Main(string[] args)
    {
        if (args.Length == 0 || (args[0] != "host" && args[0] != "guest"))
        {
            Console.Error.WriteLine("usage: Conformance host|guest [--port P] [--app NAME] [--major N] [--heartbeat-ms N] [--timeout-ms N] [--retry-ms N]");
            return 2;
        }
        var opt = new Dictionary<string, string>();
        for (int i = 1; i + 1 < args.Length; i += 2) opt[args[i]] = args[i + 1];
        string app = Get(opt, "--app", "minecraft-skylines");
        int port = int.Parse(Get(opt, "--port", "47615"));
        Action<string> log = s => Console.Error.WriteLine("log: " + s);

        Func<ushort, byte[], bool> send;
        Action<List<BridgeEvent>> poll;
        Action<ushort, string> shutdown;
        if (args[0] == "host")
        {
            var host = new BridgeHost(new HostOptions
            {
                Port = port, AppProtocol = app, PeerName = "Skylines.Bridge.Conformance host", PeerVersion = "0",
                HeartbeatIntervalMs = int.Parse(Get(opt, "--heartbeat-ms", "1000")),
                PeerTimeoutMs = int.Parse(Get(opt, "--timeout-ms", "5000")), Log = log
            });
            if (!host.Start()) return 1;
            send = host.Send; poll = host.Poll; shutdown = host.Shutdown;
        }
        else
        {
            int retry = int.Parse(Get(opt, "--retry-ms", "1000"));
            var guest = new BridgeGuest(new GuestOptions
            {
                Port = port, AppProtocol = app, AppMajor = ushort.Parse(Get(opt, "--major", "1")),
                PeerName = "Skylines.Bridge.Conformance guest", PeerVersion = "0",
                Retry = new RetrySchedule(retry, 10, 5000), Log = log
            });
            guest.Start();
            send = guest.Send; poll = guest.Poll; shutdown = guest.Shutdown;
        }

        var events = new List<BridgeEvent>();
        ThreadStart drain = () =>
        {
            events.Clear();
            poll(events);
            foreach (BridgeEvent e in events) Handle(e, send);
        };
        var pump = new Thread(() => { while (!_stop) { drain(); Thread.Sleep(5); } }) { IsBackground = true };
        pump.Start();
        Print("READY");

        Console.In.ReadToEnd();
        shutdown(GoodbyeCodes.ShuttingDown, "shutting down");
        _stop = true;
        pump.Join();
        drain();
        return 0;
    }

    private static string Get(Dictionary<string, string> opt, string key, string fallback)
    {
        string v;
        return opt.TryGetValue(key, out v) ? v : fallback;
    }

    private static void Handle(BridgeEvent e, Func<ushort, byte[], bool> send)
    {
        switch (e.Kind)
        {
            case BridgeEventKind.StateChanged:
                Print("EVENT state " + e.StateName + (e.Detail.Length > 0 ? " " + e.Detail : ""));
                break;
            case BridgeEventKind.Disconnected:
                Print("EVENT disconnected cause=" + e.CauseName + " code=" + e.Code + " reason='" + e.Reason + "'");
                break;
            case BridgeEventKind.Message:
                if (e.MessageType == EchoRequest) send(EchoReply, e.Payload);
                else Print("EVENT app type=0x" + e.MessageType.ToString("x4") + " bytes=" + e.Payload.Length);
                break;
        }
    }
}
