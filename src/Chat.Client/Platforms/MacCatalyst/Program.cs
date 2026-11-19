#if MACCATALYST
using UIKit;

namespace Chat.Client;

public class Program
{
    static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
#endif
