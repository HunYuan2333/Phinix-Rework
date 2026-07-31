using System;
using System.Collections.Generic;
using Utils.Framework;

namespace PhinixClient.Framework
{
    /// <summary>
    /// v1 静态激活策略：从用户设置读取一次性禁用快照，结合依赖图判断是否激活。
    /// 重启生效——构造时快照，运行时不可变。
    /// v2 可替换为运行时可变实现（RuntimeActivationPolicy），DiscoverExtensions 零改动。
    /// 设计哲学 §2.1 松耦合：通过 IExtensionActivationPolicy 接口解耦。
    /// 设计哲学 §1.1 插件平权：不硬编码任何"不可禁用"的扩展。
    /// </summary>
    internal sealed class StaticActivationPolicy : IExtensionActivationPolicy
    {
        private readonly HashSet<string> _disabled;
        private readonly ExtensionDependencyGraph _dependencyGraph;

        public StaticActivationPolicy(
            IEnumerable<string> userDisabledExtensions,
            ExtensionDependencyGraph dependencyGraph)
        {
            _disabled = new HashSet<string>(userDisabledExtensions ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            _dependencyGraph = dependencyGraph ?? ExtensionDependencyGraph.Build(Array.Empty<Type>());
        }

        public IReadOnlyCollection<string> DisabledExtensions => _disabled;

        public bool ShouldActivate(string extensionId, out string reason)
        {
            if (extensionId != null && _disabled.Contains(extensionId))
            {
                reason = "User disabled via Mod Settings";
                return false;
            }

            IReadOnlyList<string> disabledDeps = _dependencyGraph?.GetDisabledDependencies(extensionId, _disabled);
            if (disabledDeps != null && disabledDeps.Count > 0)
            {
                reason = "Dependencies disabled: " + string.Join(", ", disabledDeps);
                return false;
            }

            reason = null;
            return true;
        }
    }
}
