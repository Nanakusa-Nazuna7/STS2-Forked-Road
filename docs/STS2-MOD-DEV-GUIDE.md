# STS2 模组开发指南（写给下一个 agent）

本文档总结把 STS2-Forked-Road 从 v1.0.9 移植到 Slay the Spire 2 v0.107.1 的完整方法论。
适用于：给 Slay the Spire 2（或同类 Godot C# 游戏）开发/移植/修复模组的任何后续工作。

## 0. 一句话结论

**不要靠猜。先反编译游戏 DLL 建立"事实来源"，一切 API 查询以反编译树为准；每次改动后都用静态校验工具 + 游戏日志闭环验证。**

## 1. 先搞清楚"模组是怎么被加载的"

一切从游戏日志开始。日志位置：

```
%APPDATA%\SlayTheSpire2\logs\godot*.log
```

日志会按顺序打印模组加载全过程，这是"机制文档"：

```
[INFO] Found mod manifest file ...\mods\ForkedRoad-1.0.10\ForkedRoad.json
[INFO] Loading assembly DLL ...\mods\ForkedRoad-1.0.10\ForkedRoad.dll
[INFO] Calling initializer method of type ForkedRoad.ForkedRoadEntry
[INFO] ForkedRoad rebuild initialized.        <- 你的初始化代码输出
[INFO] Finished mod initialization for 'Forked Road'
```

结论：模组 = **manifest json（id/name/version + 入口类声明）+ DLL + 入口类**。
游戏是 **Godot C# + .NET 9 运行时**，补丁用 **HarmonyLib (0Harmony)**。
别猜 json 格式，直接看 `mods\` 下现有模组的 json 抄结构。

## 2. 建立事实来源：反编译游戏 DLL

游戏 DLL 在 `<游戏目录>\data_sts2_windows_x86_64\`（sts2.dll、GodotSharp.dll、0Harmony.dll 等）。

```powershell
ilspycmd "D:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64\sts2.dll" -o <decomp-dir>
```

反编译出完整源码树（本会话 3425 个文件）。之后所有"这个方法签名是什么/这个字段还在吗/这个类型去哪了"的问题，**一律 grep 反编译树**，不许凭记忆猜。

## 3. 适配方法论：新旧对照 + 逐类问题批量修

1. 旧代码（`src\`）编译，收集全部编译错误 → 错误就是 API 漂移清单。
2. 每个错误去反编译树查新签名，逐个改。
3. 同类问题**一次批量改完再构建**（效率高），但每批改完必须重新验证。

本会话遇到的具体漂移类型（可作模板）：
- **接口新增成员**：`INetMessage` 在 v0.107.1 要求 `ShouldBuffer` 属性 → 9 个结构体全部补 `ShouldBuffer => true`。
- **原生功能已存在**：PaelsWing、力量缩放覆盖补丁 → 游戏原生按分支缩放了，删掉自己的补丁（少即是多）。
- **构造函数签名变化**：`CombatState` 构造参数变了 → 对照新签名调传参。
- **类型新增/方法新增**：`BranchScopedRunState` 需要补 `BadgeModels`、`IterateHookListeners`。
- **字段变私有/消失**：`MerchantRoom.Inventories` 的 backing field 没了 → 改反射绑定私有 `_runState` 字段 + 用公开属性/方法。

## 4. Harmony 补丁红线（最容易翻车的点）

1. **前缀/后缀参数按"名字"绑定**，不按位置！参数名必须与目标方法参数名完全一致。
   实测事故：`RunManager.SetUpSavedMultiplayer(RunState state, LoadRunLobby lobby)` 的 prefix 写成
   `Prefix(RunState runState, ...)` → 启动时 `PatchAll` 直接抛
   `Parameter "runState" not found in method ...`。
2. `__instance` 绑定实例、`__result` 绑定返回值，**类型必须精确匹配**（`ref` 类型也要匹配）。
3. 泛型类型用反引号记法：`List`1`、`Dictionary`2`、`HashSet`1`、`Nullable`1`、`ReadSaveResult`1`。
4. 目标方法要先在反编译树确认存在，方法名、参数个数/顺序、类型都要对。
5. PatchAll 在入口初始化里执行 → **游戏启动即测试**：日志出现 HarmonyException 就是补丁挂了，堆栈里会写明是哪个方法。

## 5. 静态校验工具（本仓库 tools/，强烈建议复用）

**为什么**：人眼核对 127 处签名不现实，把核对交给代码。且 `MetadataLoadContext`
加载 DLL 时**不需要启动游戏进程**，迭代极快。

- `tools/verify-api/`：遍历模组 DLL 所有 `[HarmonyPatch]` 类，解析目标类型/方法，
  检查：目标存在、字段存在、prefix/postfix/finalizer 的参数名与类型逐一匹配。
  输出 `OK=<n> FAIL=<n>`，`FAIL=0` 才允许部署。环境变量 `STS2_GAME_DIR`/`STS2_MOD_DLL` 覆盖路径。
- `tools/dump-patch/`：导出模组 DLL 里所有补丁的目标签名 + 参数列表，构建后肉眼确认。

游戏升级后第一件事：跑 verify-api，FAIL 列表 = 全部待修清单。

## 6. 冒烟测试闭环（构建 → 部署 → 日志 → 修）

```
dotnet build → 拷贝到 mods\ForkedRoad-1.0.10\ → 启动游戏 → 读日志 → 修 → 重复
```

要点：
- **游戏运行时会锁部署的 DLL**：拷贝前 `Stop-Process -Name SlayTheSpire2 -Force`，等 ~5 秒再拷。
- **mods\ 下不要放两个版本目录**：游戏只扫其中一个（实测只加载旧目录），只留一个安装目录，旧的移走备份。
- **直接启动 exe 需要 `steam_appid.txt`**（内容为游戏 AppID，如 2868840），否则 Steamworks 报 "No appID found"。
- 成功判据（日志）：`rebuild initialized.` + `Finished mod initialization` + 之后无 `ERROR`/`Exception`。
- 日志尾部大量 `RID allocations ... leaked at exit` 是 Godot 退出常规噪音，不是错误。
- 别把无关 mod 的错误当成自己的（如 `MultiplayerQuickSL` 的 manifest 缺 id，与 ForkedRoad 无关）。

## 7. Git 与部署纪律

- 只推自己的 fork 分支（`port-v0.107.1`），绝不推 main。
- 仓库可能没配 git 身份：用 `git -c user.name=... -c user.email=... commit`。
- 旧安装目录移入仓库 `backup-install\` 并 gitignore。
- 部署后核对已部署 DLL 与构建输出的 SHA256 一致。
- 工具/指南这类资产收进 `tools\`、`docs\`，并补充 `.gitignore` 通配（`**/bin/`、`**/obj/`）。

## 8. 通用检查清单（开始新模组开发时逐项过）

1. 日志确认模组加载机制（manifest 格式、入口类、加载顺序）
2. ilspycmd 反编译 sts2.dll → 事实来源
3. 旧代码编译收集错误 → API 漂移清单
4. 逐类批量修复，每批重编译
5. 写/跑 verify-api（tools/），FAIL=0
6. 部署单一版本目录 + steam_appid.txt
7. 启动游戏读日志：初始化成功 + 无异常
8. 单人局实机 + 复查日志
9. 推送分支、整理工具与文档、可选开 PR
