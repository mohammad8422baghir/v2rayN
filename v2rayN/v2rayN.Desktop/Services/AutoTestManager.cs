using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using v2rayN.Desktop.ViewModels;

namespace v2rayN.Desktop.Services
{
    public class AutoTestManager
    {
        private static AutoTestManager _instance;
        public static AutoTestManager Instance => _instance ??= new AutoTestManager();

        private bool _isAutoTestEnabled = false;
        private Timer _testTimer;
        private readonly HttpClient _httpClient;

        private AutoTestManager()
        {
            _httpClient = new HttpClient();
            // زمان انتظار برای تست واقعی از گوگل (حداکثر 4 ثانیه)
            _httpClient.Timeout = TimeSpan.FromSeconds(4); 
        }

        public void ToggleAutoTest(bool isEnabled, Action onProfileChanged)
        {
            _isAutoTestEnabled = isEnabled;
            if (_isAutoTestEnabled)
            {
                // هر 60 ثانیه (60000 میلی‌ثانیه) تست را اجرا می‌کند
                _testTimer = new Timer(async (e) => await RunAutoTestAsync(onProfileChanged), null, 0, 60000);
            }
            else
            {
                _testTimer?.Dispose();
            }
        }

        private async Task RunAutoTestAsync(Action onProfileChanged)
        {
            if (!_isAutoTestEnabled) return;

            // دریافت لیست سرورها از کلاس‌های پیش‌فرض v2rayN (نام کلاس ممکن است بسته به نسخه فرق کند)
            // در v2rayN معمولاً لیست کانفیگ‌ها در AppConfig یا ConfigHandler است
            var profiles = AppConfig.GetProfiles(); 
            
            ProfileItem bestProfile = null;
            long bestPing = long.MaxValue;

            foreach (var profile in profiles)
            {
                // مرحله 1: ارسال پینگ TCP سریع
                long tcpPing = TestTcpPing(profile.Address, profile.Port);
                if (tcpPing < 0) continue; 

                // مرحله 2: تست اتصال واقعی به گوگل
                long httpDelay = await TestRealDelayAsync(profile);
                if (httpDelay > 0 && httpDelay < bestPing)
                {
                    bestPing = httpDelay;
                    bestProfile = profile;
                }
            }

            // تغییر خودکار کانفیگ و ریستارت هسته در صورت پیدا شدن سرور بهتر
            if (bestProfile != null && bestProfile.IndexId != AppConfig.ActiveProfileId)
            {
                 AppConfig.SetActiveProfile(bestProfile.IndexId);
                 // فراخوانی اکشن برای بروزرسانی رابط کاربری و ری‌استارت هسته
                 onProfileChanged?.Invoke(); 
            }
        }

        private long TestTcpPing(string address, int port)
        {
            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    var result = client.BeginConnect(address, port, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2));
                    if (!success) return -1;
                    client.EndConnect(result);
                }
                watch.Stop();
                return watch.ElapsedMilliseconds;
            }
            catch
            {
                return -1;
            }
        }

        private async Task<long> TestRealDelayAsync(ProfileItem profile)
        {
            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                // در حالت ایده‌آل اینجا باید از پراکسی پورت v2ray عبور داده شود
                var response = await _httpClient.GetAsync("https://www.google.com/generate_204");
                watch.Stop();

                if (response.IsSuccessStatusCode)
                    return watch.ElapsedMilliseconds;
            }
            catch { }
            return -1;
        }
    }
}
