# rooms-compliance — 建筑运维房间合规检查

本技能用于检查 Revit 模型中的**房间(Room)**是否符合建筑运维使用需求。检查项:

1. 房间是否已放置(有位置)
2. 房间是否已封闭(面积 > 0,边界完整)
3. 房间是否有"房间编号"参数(自定义属性字段)
4. 房间编号参数是否有值(非空)
5. 房间编号值是否重复(运维要求编号唯一)

## 何时使用

当用户需求涉及以下任一情况时,优先加载本技能再生成代码:

- "检查房间是否符合建筑运维要求"
- "房间编号有没有重复"
- "房间是否都有编号"
- "有没有未放置 / 未封闭的房间"
- "建筑运维房间合规检查"

## 业务约定

- **已放置(IsPlaced)**:`room.Location != null`。未放置的房间运维无法定位,视为不合格。
- **已封闭(IsEnclosed)**:`room.Area > 0`。未封闭房间面积 = 0,边界未围合。
- **房间编号参数**:不是 `room.Number`(系统默认编号),而是名为"房间编号"的自定义参数(共享参数或项目参数)。用 `room.GetParameters("房间编号")` 查找。
  - 运维要求:每个房间必须有"房间编号"参数(字段存在性)。
  - 参数必须有值(非空字符串)。
  - 房间编号值在全模型内唯一(不重复)。
- **房间类型**:`Autodesk.Revit.DB.Architecture.Room`,类别 `BuiltInCategory.OST_Rooms`。
- **所属楼层**:`document.GetElement(room.LevelId)?.Name`。

## 不合格判定

未放置 / 未封闭 / 无"房间编号"参数 / 参数值为空 / 编号重复 → 任一命中即该房间不合格。

## 输出要求

返回匿名对象,字段:

- `RoomCount`:房间总数
- `CompliantCount`:合格房间数(满足全部 5 项)
- `NonCompliantCount`:不合格房间数
- `UnplacedCount`:未放置数
- `UnenclosedCount`:未封闭数
- `MissingNumberParamCount`:缺"房间编号"参数的数
- `EmptyNumberValueCount`:编号值为空的数
- `DuplicateNumberCount`:编号重复的房间数(值相同的房间总数,不是去重后的组数)
- `DuplicateGroups`:重复编号分组(编号值 → 房间数,前 20 组)
- `Issues`:不合格房间明细(前 50 个,字段:Id/Number/Name/LevelName/Area/IsPlaced/IsEnclosed/HasNumberParam/NumberValue/Issue 原因)

供 agent 据此向用户汇报。

## 模板

`templates/RoomsComplianceCheck.cs` 实现 `IRevitDynamicCommand`,完成上述检查并返回可序列化数据。采用两轮遍历:第一轮收集每房间的检查数据 + 编号值,第二轮按"值重复集合"判定每个房间是否合格。可直接使用,或按需扩展(如按楼层汇总、筛特定楼层)。
