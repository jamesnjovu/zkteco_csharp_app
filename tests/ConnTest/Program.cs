using System;
using System.Threading;
using zkemkeeper;

Console.WriteLine($"Main thread apartment: {Thread.CurrentThread.GetApartmentState()}");

// Test 1: Direct on main thread (MTA)
Console.WriteLine("\n--- Test 1: MTA thread ---");
try
{
    var czkem = new CZKEM();
    Console.WriteLine("CZKEM created");
    bool ok = czkem.Connect_Net("10.121.0.206", 4370);
    Console.WriteLine($"Connect_Net: {ok}");
    if (!ok) { int e = 0; czkem.GetLastError(ref e); Console.WriteLine($"Error: {e}"); }
    else { Console.WriteLine("CONNECTED!"); czkem.Disconnect(); }
}
catch (Exception ex) { Console.WriteLine($"Exception: {ex.Message}"); }

// Test 2: On STA thread
Console.WriteLine("\n--- Test 2: STA thread ---");
var done = new ManualResetEventSlim();
string? result = null;
var sta = new Thread(() =>
{
    try
    {
        Console.WriteLine($"STA thread apartment: {Thread.CurrentThread.GetApartmentState()}");
        var czkem = new CZKEM();
        Console.WriteLine("CZKEM created on STA");
        bool ok = czkem.Connect_Net("10.121.0.206", 4370);
        Console.WriteLine($"Connect_Net: {ok}");
        if (!ok) { int e = 0; czkem.GetLastError(ref e); Console.WriteLine($"Error: {e}"); }
        else { Console.WriteLine("CONNECTED!"); czkem.Disconnect(); }
    }
    catch (Exception ex) { Console.WriteLine($"Exception: {ex.Message}"); }
    finally { done.Set(); }
});
sta.SetApartmentState(ApartmentState.STA);
sta.Start();
done.Wait(TimeSpan.FromSeconds(15));
Console.WriteLine("Done.");
