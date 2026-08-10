using System;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using PhinixClient;
using PhinixClient.Framework;
using Utils;
using Utils.Framework;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    /// <summary>
    /// 人才贸易插件入口（Rework 插件化：IPhinixExtensionModule）。
    ///
    /// 设计哲学 §1.1 插件平权；§3.7 协议经中继（Legacy 适配器例外），不注入框架管线。
    /// Harmony 补丁挂载约束（与禁用机制联动）：PatchAll 在 Activate，UnpatchAll 在 Shutdown；
    /// 禁止静态构造器挂补丁（程序集被扩展扫描加载时会绕过禁用）。
    /// </summary>
    [PhinixExtension("builtin.legacy-talent-trade")]
    public sealed class BuiltInLegacyTalentTradeClientExtension : IPhinixExtensionModule, IActivatablePhinixExtensionModule
    {
        private const string HarmonyId = "iniad.legacytalenttrade";

        private Harmony harmony;
        private System.Timers.Timer driveTimer;
        private bool activated;
        // 防重复入队：队列中已有待执行的 Update 时不再入队（Timer 200ms 远快于主线程消费，
        // 若无此标志，主线程繁忙/卡顿时队列会积压到溢出）
        private int updatePending;

        public string ExtensionId => "builtin.legacy-talent-trade";

        public void Register(IExtensionBuilder builder)
        {
            LegacyTalentTradeRuntime.Session = builder.HostContext.GetRequiredService<IClientSessionContext>();
            LegacyTalentTradeRuntime.Users = builder.HostContext.GetRequiredService<IClientUserDirectory>();
            LegacyTalentTradeRuntime.Dispatcher = builder.HostContext.GetRequiredService<IClientMainThreadDispatcher>();
            LegacyTalentTradeRuntime.SettingsContext = builder.HostContext.GetRequiredService<IClientSettingsContext>();
            LegacyTalentTradeRuntime.Settings.Load(LegacyTalentTradeRuntime.SettingsContext);
            LegacyTalentTradeRuntime.Log = builder.HostContext.Log;

            builder.RegisterApi<IMainTabProvider>(TalentTradeTab.Instance);
            builder.RegisterApi<IClientSettingsPanelProvider>(new LegacyTalentTradeSettingsPanel());
        }

        public void Activate(ExtensionHostContext hostContext)
        {
            if (activated) return;
            activated = true;
            LegacyTalentTradeRuntime.IsActive = true;

            TalentTradeManager.Initialize(hostContext.GetRequiredService<IClientUserEventStream>());

            // 游戏级补丁（GenScene 退出下架 / PawnTextureAtlasGC 修复）——禁用时不会执行 Activate，补丁零挂载
            harmony = new Harmony(HarmonyId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            driveTimer = new System.Timers.Timer
            {
                AutoReset = true,
                Interval = 200
            };
            driveTimer.Elapsed += OnDriveTick;
            driveTimer.Start();

            hostContext.Log?.Invoke("[TalentTrade] Legacy talent trade extension activated.", LogLevel.INFO);
        }

        private void OnDriveTick(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!activated || !LegacyTalentTradeRuntime.IsActive) return;
            if (Interlocked.CompareExchange(ref updatePending, 1, 0) != 0) return;
            if (LegacyTalentTradeRuntime.Dispatcher == null)
            {
                Interlocked.Exchange(ref updatePending, 0);
                return;
            }

            LegacyTalentTradeRuntime.Dispatcher.Enqueue(() =>
            {
                try
                {
                    TalentTradeManager.Update();
                }
                finally
                {
                    Interlocked.Exchange(ref updatePending, 0);
                }
            });
        }

        public void Shutdown(ExtensionHostContext hostContext)
        {
            if (!activated) return;
            activated = false;
            LegacyTalentTradeRuntime.IsActive = false;

            if (driveTimer != null)
            {
                driveTimer.Stop();
                driveTimer.Elapsed -= OnDriveTick;
                driveTimer.Dispose();
                driveTimer = null;
            }

            TalentTradeManager.Shutdown();
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;

            hostContext.Log?.Invoke("[TalentTrade] Legacy talent trade extension shut down.", LogLevel.INFO);
        }
    }
}
