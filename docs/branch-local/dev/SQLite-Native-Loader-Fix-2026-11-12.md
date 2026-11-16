# SQLite 原生库目录与 RimWorld 扫描边界修复

日期：2026-11-12。此前 11 月 10 日测试包将原生文件放入 `<game>/Assemblies/runtimes`，这是错误的发行布局。旧包静态审计刻意排除了 runtimes，因而没有模拟游戏实际的递归扫描。用户两份日志均在 ModAssemblyHandler.ReloadAll 中报 e_sqlite3.dll BadImageFormatException；各自仅一条 ERROR，不能将该启动扫描错误归因于数据库表/事务。

## 已核对本地游戏代码

对 `GameDlls/Assembly-CSharp.dll` 的 1.6、私有 1.7 编译引用及本机 Steam 当前 `Assembly-CSharp.dll` 做了 Mono.Cecil IL 读取。本机 Steam 1.7 程序集身份为 `Assembly-CSharp, Version=1.7.9810.32479`。

- `Verse.ModAssemblyHandler.ReloadAll()` 以 `Assemblies/` 调用 `ModContentPack.GetAllFilesForModPreserveOrder`。
- 该方法对有效加载目录组合子路径，使用 `DirectoryInfo.GetFiles("*.*", SearchOption.AllDirectories)`；IL 中 SearchOption 的实参为常量 1。
- ReloadAll 的编译器过滤委托只比较扩展名 ToLower() 与 `.dll`。
- 过滤后直接 `Assembly.LoadFrom(file.FullName)`。1.7 当前程序集 IL_0069 调用 LoadFrom；没有原生/托管检测，也没有 runtimes 特例。
- `runtimes/win-arm64` 只是先撞到的文件。任意平台的 Windows 原生 SQLite DLL 都不应进入该托管扫描根。

另在独立进程中直接调用真实游戏的 `GetAllFilesForModPreserveOrder`，反射构造仅含加载目录的 ModContentPack：

| 版本 | 旧包扫描 DLL / SQLite native | 新包扫描 DLL / SQLite native |
| --- | --- | --- |
| 1.6 | 30 / 3 | 27 / 0 |
| 1.7 | 24 / 3 | 21 / 0 |

旧包枚举出的原生 DLL 经 AssemblyLoadContext 加载实际抛 BadImageFormatException；修复包不再被枚举。这是实际游戏枚举方法和 CLR 加载行为验证，不是 Windows 图形游戏完整启动验收。

## 修复

原生库移为 `<game>/Native/runtimes/<rid>/native`。`SqliteRuntime` 从托管库所在 Assemblies 的同级 Native 按当前 OS/进程架构解析；不修改游戏加载器、不删 ARM64 支持、不忽略引擎错误。普通 SDK 测试输出在 Assemblies 之外才允许使用本地 runtimes 回退，发行文件缺失时明确失败。

更新原始 net472 Host 复制目标为 Common/Native、双版本 staging 和独立插件构建的 managed/native 分类。最终 ZIP 及 staging 增加 CLR PE 头检查；Assemblies 下所有嵌套 DLL 都必须是托管代码，不再跳过 runtimes。

## 验证与制品

- 6 项发行元数据/扫描边界单元测试通过。
- SQLite 基础 22 项在 net10 和 Mono 两种运行时，分别按 SDK 布局与发行布局运行，四次均通过。
- 实际新生产 Phinix.ClientStorage.dll 在 1.6/1.7 发行目录关系下，真实创建 SQLite、写入、读取、关闭重开通过（Linux dotnet/Mono）。
- 原始错误 ZIP 被新增验证器拒绝；修复包 CRC、静态资源与文件完整性通过。
- 新包以已交付迁移弹窗 ZIP 为不可变基线，仅替换两份存储实现 DLL、移动原生文件；逐字证明其余所有文件与 native 内容不变，不混入 11 月 12 日其他工作区改动。未更改 schema、库存余额、用户数据位置或历史迁移逻辑。

制品及证据：`/home/hunyuan2333/Phinix/Deliverables/Phinix-Rework-SQLite-Native-Fix-2026-11-12`。本次未上传工坊。

安装或上传时必须使用清空后的模组发布目录；只覆盖新 ZIP 可能保留旧 Assemblies/runtimes，使同一错误继续发生。不得删除用户数据中的 SQLite 或存档。Windows 游戏完整启动仍待实机复测。
