using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Text;
using System.IO;
using System.Web;

namespace BiliDymicBot
{
    class Program
    {
        public static HttpClient http;
        public static string lastDynamicId = null;
        public static Timer timer;
        public static HookBot bot;
        public static bool EnableStatePush;
        public static int RetryCount = 0;
        static void Main(string[] args)
        {
            if (!File.Exists("config.json"))
            {
                var configStr = JsonConvert.SerializeObject(new Config(),Formatting.Indented);
                File.WriteAllText("config.json", configStr);
                System.Environment.Exit(0);
            }
            var conf = JsonConvert.DeserializeObject<Config>(File.ReadAllText("config.json"));
            EnableStatePush = conf.EnableStatePush;
            bot = new HookBot(conf.QqHookUrl);
            CookieContainer cookieContainer = new CookieContainer();
            HttpClientHandler handler = new HttpClientHandler() { UseCookies = true };
            handler.CookieContainer = cookieContainer;
            http = new HttpClient(handler);
            if (Login()) Console.WriteLine("Login success.");
            else
            {
                Console.WriteLine("Login fail. Press any key to exit.");
                Console.ReadKey();
                System.Environment.Exit(0);
            }
            var djo= GetDynamic();
            lastDynamicId = djo["data"]["cards"][2]["desc"]["dynamic_id"].ToString();
            Console.WriteLine("Init success.");
            if (EnableStatePush) bot.SendMessage("B站动态推送机器人正在运行！");
            timer = new Timer(TimerCallBack, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            var nextTime = DateTime.Now.AddSeconds(3);
            timer.Change(nextTime.Subtract(DateTime.Now), Timeout.InfiniteTimeSpan);
            while (true)
            {
                var comStr = Console.ReadLine();
                if (comStr == "exit")
                {
                    if (EnableStatePush) bot.SendMessage("机器人停止运行");
                    System.Environment.Exit(0);
                }
            }
        }
        static bool Login()
        {
            var t = http.GetAsync(new Uri(@"http://passport.bilibili.com/qrcode/getLoginUrl"));
            t.Wait();
            var result = t.Result;
            var t2 = result.Content.ReadAsStringAsync();
            t2.Wait();
            var s = t2.Result;
            var jo = JsonConvert.DeserializeObject<JObject>(s);
            string qrUrl = jo["data"]["url"].ToString();
            string oauthKey= jo["data"]["oauthKey"].ToString();
            var encodQrUrl= HttpUtility.UrlEncode(qrUrl, Encoding.UTF8);
            Console.WriteLine("QL Code Url is:\nhttp://qr.liantu.com/api.php?text=" + encodQrUrl);
            Console.WriteLine("Please press Enter after you confirm your login. The time limit is 180s.");
            Console.ReadLine();
            var postValuePairs = new List<KeyValuePair<string, string>>();
            postValuePairs.Add(new KeyValuePair<string, string>("oauthKey", oauthKey));
            var encodedContent = new FormUrlEncodedContent(postValuePairs);
            t = http.PostAsync(new Uri(@"http://passport.bilibili.com/qrcode/getLoginInfo"),encodedContent);
            t.Wait();
            result = t.Result;
            t2 = result.Content.ReadAsStringAsync();
            t2.Wait();
            s = t2.Result;
            jo = JsonConvert.DeserializeObject<JObject>(s);
            var status = jo["status"].ToString();
            if (status == "True")
            {
                return true;
            }
            else return false;
        }
        static JObject GetDynamic()
        {
            var t = http.GetAsync(new Uri(@"https://api.vc.bilibili.com/dynamic_svr/v1/dynamic_svr/dynamic_new?uid=37366367&type_list=268435455"));
            t.Wait();
            var result = t.Result;
            var t2 = result.Content.ReadAsStringAsync();
            t2.Wait();
            var s = t2.Result;
            var jo = JsonConvert.DeserializeObject<JObject>(s);
            return jo;
        }
        static string GetBvid(string aid)
        {
            var http = new HttpClient();
            var t = http.GetAsync(new Uri(@"http://api.bilibili.com/x/web-interface/archive/stat?aid="+aid));
            t.Wait();
            var result = t.Result;
            var t2 = result.Content.ReadAsStringAsync();
            t2.Wait();
            var s = t2.Result;
            var jo = JsonConvert.DeserializeObject<JObject>(s);
            var dataJo = JsonConvert.DeserializeObject<JObject>(jo["data"].ToString());
            var bvid = dataJo["bvid"].ToString();
            return bvid;
        }
        static void TimerCallBack(Object state)
        {
            var djo= GetDynamic();
            JArray dja = null;
            string lastdid = null;
            try
            {
                dja = djo["data"]["cards"].ToObject<JArray>();
                lastdid = dja[0]["desc"]["dynamic_id"].ToString();
                Console.WriteLine("Updated, last did is " + lastdid);
                RetryCount = 0;
            }
            catch
            {
                if (RetryCount == 0)
                {
                    Console.WriteLine("动态拉取失败，等待重试");
                    if (EnableStatePush) bot.SendMessage("动态拉取失败，等待重试");
                    RetryCount++;
                }
                else if (RetryCount == 3)
                {
                    Console.WriteLine("动态拉取失败，等待重试");
                    if (EnableStatePush) bot.SendMessage("动态拉取失败次数过多，机器人已经退出");
                    RetryCount++;
                }
                else
                {
                    Console.WriteLine("动态拉取失败，等待重试 第"+RetryCount+"次");
                }
                var nnextTime = DateTime.Now.AddSeconds(60);
                timer.Change(nnextTime.Subtract(DateTime.Now), Timeout.InfiniteTimeSpan);
                return;
            }
            for (var count = 0; count < dja.Count; count++)
            {
                string did = dja[count]["desc"]["dynamic_id"].ToString();
                bool successf = false;
                if (did == lastDynamicId) break;
                var sb = new StringBuilder();
                sb.Append("UP主:");
                sb.Append(dja[count]["desc"]["user_profile"]["info"]["uname"].ToString());
                sb.Append("\n");
                string cardStr = dja[count]["card"].ToString();
                JObject cjo = JsonConvert.DeserializeObject<JObject>(cardStr);
                //for dynamic with picture
                if (!successf)
                {
                    try
                    {
                        var ndid = cjo["item"]["id"].ToString();
                        var desc = cjo["item"]["description"].ToString();
                        //string result = Uri.UnescapeDataString(desc);
                        sb.Append("发表了动态（有图片）\n");
                        sb.Append(desc);
                        successf = true;
                    }
                    catch { }
                }
                //for pure dymic
                if (!successf)
                {
                    try
                    {
                        var desc = cjo["item"]["content"].ToString();
                        //string result = Uri.UnescapeDataString(desc);
                        sb.Append("发表了动态\n");
                        sb.Append(desc);
                        try
                        {
                            var fuser = cjo["origin_user"]["info"]["uname"].ToString();
                            sb.Append("\n>>>转发自：");
                            sb.Append(cjo["origin_user"]["info"]["uname"].ToString());
                            sb.Append("的动态");
                            var oContent = cjo["origin"].ToString();
                            JObject originJo = JsonConvert.DeserializeObject<JObject>(oContent);
                            var osucf = false;
                            if (!osucf)
                            {
                                try
                                {
                                    var ndid = originJo["item"]["id"].ToString();
                                    var ddesc = originJo["item"]["description"].ToString();
                                    //string result = Uri.UnescapeDataString(desc);
                                    sb.Append("\n>>>");
                                    sb.Append(ddesc.Replace("\r\n","\n>>>").Replace("\n", "\n>>>"));
                                    osucf = true;
                                }
                                catch { }
                            }
                            if (!osucf)
                            {
                                try
                                {
                                    var ddesc = originJo["item"]["content"].ToString();
                                    //string result = Uri.UnescapeDataString(desc);
                                    sb.Append("\n>>>");
                                    sb.Append(ddesc.Replace("\r\n", "\n>>>").Replace("\n", "\n>>>"));
                                }
                                catch { }
                            }
                            if (!osucf)
                            {
                                try
                                {
                                    var vCount = originJo["videos"].ToString();
                                    var vTitle = originJo["title"].ToString();
                                    var aid = originJo["aid"].ToString();
                                    sb.Append("\n");
                                    sb.Append(">>>发布了视频：");
                                    sb.Append(vTitle);
                                    sb.Append("\n>>>");
                                    sb.Append(GetBvid(aid));
                                    sb.Append("\n>>>");
                                    sb.Append(originJo["dynamic"].ToString().Replace("\r\n", "\n>>>").Replace("\n", "\n>>>"));
                                    osucf = true;
                                }
                                catch { }
                            }
                            if (!osucf)
                            {
                                try
                                {
                                    var authNmae = originJo["author"]["name"].ToString();
                                    var cvTitle = originJo["title"].ToString();
                                    sb.Append("\n");
                                    sb.Append(">>>发表了专栏：");
                                    sb.Append(cvTitle);
                                    sb.Append("\n>>>");
                                    sb.Append(originJo["dynamic"].ToString().Replace("\r\n", "\n>>>").Replace("\n", "\n>>>"));
                                    osucf = true;
                                }
                                catch { }
                            }

                        } catch { }
                        successf = true;
                    } catch { }
                }
                if (!successf)
                {
                    try
                    {
                        var vCount = cjo["videos"].ToString();
                        var vTitle = cjo["title"].ToString();
                        var aid = cjo["aid"].ToString();
                        sb.Append("发布了视频：");
                        sb.Append(vTitle);
                        sb.Append("\n");
                        sb.Append(GetBvid(aid));
                        sb.Append("\n");
                        sb.Append(cjo["dynamic"].ToString());
                        successf = true;
                    }
                    catch { }
                }
                if (!successf)
                {
                    try
                    {
                        var authNmae = cjo["author"]["name"].ToString();
                        var cvTitle = cjo["title"].ToString();
                        sb.Append("发表了专栏：");
                        sb.Append(cvTitle);
                        sb.Append("\n");
                        sb.Append(cjo["dynamic"].ToString());
                        successf = true;
                    }
                    catch { }
                }
                sb.Append("\nt.bilibili.co@m/h5/dynamic/detail/");
                sb.Append(did);
                var pushStr = sb.ToString();
                Console.WriteLine(pushStr);
                if (!successf)
                {
                    if (EnableStatePush) bot.SendMessage("啊哦，动态解析出错了");
                    Console.WriteLine("啊哦，动态解析出错了");
                    return;
                }
                bot.SendMessage(pushStr);
            }
            lastDynamicId = lastdid;
            var nextTime = DateTime.Now.AddSeconds(60);
            timer.Change(nextTime.Subtract(DateTime.Now), Timeout.InfiniteTimeSpan);
        }
    }
    class Config
    {
        public string QqHookUrl = String.Empty;
        public bool EnableStatePush = true;
    }
    class HookBot
    {
        readonly Uri QqHookUrl;
        public HookBot(string url)
        {
            QqHookUrl = new Uri(url);
        }
        public bool SendMessage(string msg)
        {
            var http = new HttpClient();
            msg = msg.Replace("\r", String.Empty).Replace("\\", @"\\").Replace("\"", "\\\"").Replace("\n", @"\n");
            var postStr = "{\"content\": [ {\"type\":0,\"data\":\"" + msg + "\"}]}";
            var content = new StringContent(postStr);
            var t = http.PostAsync(QqHookUrl, content);
            t.Wait();
            var result = t.Result;
            var t2 = result.Content.ReadAsStringAsync();
            t2.Wait();
            var s = t2.Result;
            if (s != "") return false;
            else return true;
        }
    }
}
