# mep-device-numbers — 机电设备编号合规检查

本技能用于筛选 Revit 模型中所有有"设备编号"参数的构件(即建筑运维认定的"设备"),并检查编号合规性。

## 何时使用

当用户需求涉及以下任一情况时,优先加载本技能再生成代码:

- "检查设备编号有没有重复"
- "设备编号是不是都填了"
- "设备编号是不是实例参数"
- "筛选模型里所有设备"
- "机电设备编号合规检查"

## 业务约定

- **设备认定**:有"设备编号"参数的构件就是建筑运维中的设备。遍历所有 `FamilyInstance`,用 `inst.GetParameters("设备编号")` 或 `inst.Symbol.GetParameters("设备编号")` 筛选。
- **参数值**:`Parameter.AsString()` 取字符串值;为空时 fallback 到 `AsValueString()`。
- **参数类型判定**:
  - 实例参数:`inst.GetParameters("设备编号")` 非空。
  - 类型参数:`inst.Symbol.GetParameters("设备编号")` 非空。
  - **合规要求**:必须是实例参数,不能是类型参数。只要存在类型参数版(无论是否同时有实例版),即不合规——因为类型参数意味着同族同型号共用一个编号,运维无法定位单体。
- **编号唯一**:全模型内编号值不重复。

## 不合格判定

- "设备编号"是类型参数(非实例参数) → 不合格
- 编号值为空 → 不合格
- 编号值重复 → 不合格

## 输出要求

返回匿名对象,字段:

- `DeviceCount`:设备总数(有"设备编号"参数的构件数)
- `CompliantCount`:合格数
- `NonCompliantCount`:不合格数
- `TypeParamCount`:参数为类型参数的设备数
- `EmptyValueCount`:编号值为空的设备数
- `DuplicateNumberCount`:编号重复的设备总数
- `DuplicateGroups`:重复编号分组(编号值 → 设备数,前 20 组)
- `CategoryBreakdown`:按类别汇总(类别 → 设备数,前 20)
- `Issues`:不合格设备明细(前 50 个,字段:Id/Category/FamilyName/TypeName/HasInstanceParam/HasTypeParam/DeviceNumber/Issue 原因)

## 模板

`templates/DeviceNumbersCheck.cs` 实现 `IRevitDynamicCommand`,完成上述检查。用 nested `DeviceInfo` 类存储中间数据,两轮遍历:先收集 + 查重,再判定。可直接使用或按需扩展。
