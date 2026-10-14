# 编译 RimWorld 1.6/1.7 双版本 Host

当前双版源码使用1.6/net472及1.7/net10.0，API3；一次构建产生一个Mod目录/ZIP，由LoadFolders按游戏选择。以下入口生成Release构建的本地发行候选，不安装、不推送、不发布，不代替游戏验收。

需要.NET10 SDK、Python3、已恢复的固定源码依赖及NuGet编译依赖，以及核对版本的游戏/Harmony私有编译引用。游戏引用不会入包。入口仅在子进程指定已安装SDK，不修改vendored protobuf pins。

在当前工作区执行：

```sh
cd /home/hunyuan2333/Phinix/Phinix-Rework

python3 tools/compatibility/build-dual-host.py \
  --game16 ./GameDlls \
  --game17 ../local-compatibility-analysis/references/1.7.4977-rev1248 \
  --harmony17 ../local-compatibility-analysis/references/1.7.4977-rev1248/0Harmony.dll \
  --output /tmp/phinix-dual-build-01
```

`--game17` 指已核对的1.7程序集目录（必须含Assembly-CSharp.dll及UnityStubs.dll），`--harmony17` 为匹配的0Harmony.dll。示例用本地保留的1.7.4977 rev1248缓存；换游戏构建时重新核验引用及游戏行为，不把旧缓存当成最新安装。输出目录必须全新，位于工作区与引用目录之外；下次可换成 `/tmp/phinix-dual-build-02`。

主要产物：

```text
/tmp/phinix-dual-build-01/phinix-rework-dual-release-candidate.zip
/tmp/phinix-dual-build-01/result.json
/tmp/phinix-dual-build-01/commands.json
/tmp/phinix-dual-build-01/staged/assembly-audit.json
/tmp/phinix-dual-build-01/staged/reference-closure.json
```

ZIP内为 `phinix-rework/`，静态资源共用，DLL分别在1.6/1.7目录。没有Common跨TFM DLL混装，没有独立红包/人才/Example重复bundle，没有游戏/Harmony DLL。About显示 `Phinix Rework` 和原有简介，声明两版支持；构建/验收状态保留在开发记录中。产品二进制版本来自当前源码，不擅自分配正式版本。

入口顺序：隔离完整1.6源图及三个独立插件编译；隔离完整1.7源图；从仓库源码新编译元数据检查工具；检查TFM、源身份/引用与禁止入包的游戏程序集；合并目录并CRC检查ZIP。保留工作区bin/obj/Output与既有制品。独立插件仅编译验证，不生成/内置其托管ZIP。

原命令 `dotnet build Phinix.sln --configuration "Release 1.6" -p:BuildInParallel=false -m:1` 仍是1.6构建，不生成1.7；核验见 `branch-local/dev/Release-1.6-Command-Verification-2026-10-14.md`。

## 独立插件与 main 自动发行

红包、人才贸易和 Example 的本地 workflow 已升级为 API3 双版本流程，仍由 main push 自动分配 patch、发布一个不可覆盖的 ZIP，再通知 Index。尚未推送或线上运行。先校验配置、源码和固定 Host 能力，下载并校验两版私有引用，再分配正式版本。旧 Host pin 与 references17=null 会阻断；须等待真实最终 Host/Common 提交及已有私有引用资产，不能填写假 SHA。游戏 DLL 不得公开入包或发布。

在独立插件仓库中运行（红包、人才贸易和 Example 使用相同入口）：

```sh
python3 pack.py \
  --phinix-root /home/hunyuan2333/Phinix/Phinix-Rework \
  --game-references /home/hunyuan2333/Phinix/Phinix-Rework/GameDlls \
  --game17-references /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248 \
  --harmony17 /home/hunyuan2333/Phinix/local-compatibility-analysis/references/1.7.4977-rev1248/0Harmony.dll \
  --version <本地候选三段版本> \
  --output /tmp/<插件名>-dual-candidate.zip
```

输出必须不存在。该入口隔离构建两版，分别校验真实 Host/游戏引用绑定，再合成同版本 variants 包；不调用 GitHub、不分配线上版本、不发布。每个插件仍保留独立安装 ZIP，不能把 Host DLL 混入插件。

Index 的 catalog schema3 和资产命名无需改版，但静态校验器和版本/语言投影必须支持 schema2 包。旧单版/API2 首次扩展到双版/API3 需一次新的范围审核；后续已审核双版范围内的 patch 继续自动更新。实现、验证与上线缺项见 [双版本发布流程记录](branch-local/dev/Dual-Runtime-Automatic-Release-2026-10-15.md)。
