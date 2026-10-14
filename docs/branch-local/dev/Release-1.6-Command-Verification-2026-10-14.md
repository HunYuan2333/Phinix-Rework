# 原1.6发行构建命令核验（2026-10-14）

用户询问在 Phinix-Rework 执行：

```sh
dotnet build Phinix.sln --configuration "Release 1.6" -p:BuildInParallel=false -m:1
```

在 `/tmp/phinix-release16-command-check/Phinix-Rework` 的全新隔离源码副本中原样执行，无额外MSBuild属性或源码修补；仅隔离副本使用保留的GameDlls/.nuget只读引用与已缓存NuGet包。Client/Extensions/Dependencies的实际C#/工程源码与工作区逐文件hash一致。没有覆盖工作区bin/obj/Output或游戏目录，没有发布。

**编译通过**：exit0，0错误、25警告，约30秒。警告包括缓存环境NuGet漏洞信息请求失败、无Git副本的SourceLink信息和vendored protobuf旧目标裁剪提示。未修改protobuf pins或源代码消除警告。

**制品检查通过**：31个包内DLL。Host/SDK/呈现合同/内置插件为net472；protobuf保持net45，第三方库为其原兼容目标。虽solution同时生成某些依赖其他TFM输出，最终包未复制net10产物。`Output/phinix-rework` 包含Common与1.6目录、About/Defs/语言/主题/贴图及项目已有源码拷贝，不含1.7目录。

依据：`Client/Source/Client.csproj` 的 `GameVersion` 按配置空格后段解析、v4.7.2目标、版本化Host与Common依赖复制目标；`Client/versions.txt` 仅v1.6；`Client/About/About.xml` 仅声明1.6。`Client/Source/Properties/AssemblyInfo.cs` 当前产品文件/信息版本0.10.1；公开SDK `ClientEnvironmentSnapshot.cs` 为API3.0.0。

**结论**：命令仍能产生当前源码的1.6 Release构建，不能仅凭它宣称最终双版本正式发行完成。不会构建1.7/net10或生成独立红包/人才/Example ZIP；也不会把API3源码变回旧API2发行。

正式发行剩余事项：把两版构建/隔离合并及独立双版ZIP纳入可复现正式入口，确认产品/支持声明；先形成真实最终Host提交，再按其SHA升级三插件正式固定引用/API范围及打包流程。目前插件ci仍pin783fcb0，红包pack.py仍限定API2/net472；不得直接用旧流程发API3产品，不填虚假Host SHA。正式主模组/插件版本不得与旧资产或本地候选混用。最新UI/图标、业务回执/保存生命周期实机记录仍需收口；本轮编译核验不是游戏验收或发布授权。

本地日志/程序集元数据留在临时分析目录，不提交原始玩家日志、游戏DLL或私有编译引用。
