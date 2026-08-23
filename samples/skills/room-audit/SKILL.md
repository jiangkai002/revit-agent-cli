# room-audit — 房间审计

本技能用于审计 Revit 模型中的**房间（Room）**：统计总数、已放置数、已封闭数、面积分布，并标记问题房间（未放置 / 未封闭）。适配含二结构墙体、构造柱的复杂建筑模型。

## 何时使用
当用户需求涉及以下任一情况时，优先加载本技能再生成代码：
- "检查模型里房间有没有都放好 / 都封闭"
- "统计房间数量和面积"
- "有没有未封闭的房间"
- "房间是否完整"

## 业务约定
- **已放置（IsPlaced）**：`room.Location != null`。房间未被边界围合或未放置时 Location 为空。
- **已封闭（IsEnclosed）**：`room.Area > 0`。Revit 在房间被完整围合后计算面积；未封闭房间面积为 0。
- 房间类型：`Autodesk.Revit.DB.Architecture.Room`，类别 `BuiltInCategory.OST_Rooms`。
- 所属楼层：`document.GetElement(room.LevelId)?.Name`。
- 楼层名可能含前缀（如 `S_B3_基础...`、`F_01_...`），按前缀汇总可帮助核对各层房间数。

## 输出要求
返回匿名对象，字段：`RoomCount`、`PlacedRoomCount`、`EnclosedRoomCount`、`UnplacedRoomCount`、`UnenclosedRoomCount`、`Rooms`（前 20 个的明细：Id/Number/Name/LevelName/Area/IsPlaced/IsEnclosed）。供 agent 据此向用户汇报。

## 模板
`templates/RoomCheck.cs` 是可直接使用或按需改写的起点（已实现 `IRevitDynamicCommand`，返回可序列化数据）。若需求只是"统计房间"，可直接或微调后交给 `RunRevitCode`；若需按楼层汇总或筛选特定房间，在此基础上扩展。
