using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitAgent.DynamicCode;

// mep-device-numbers 技能模板:机电设备编号合规检查。
// 检查项:筛选有"设备编号"参数的构件、参数值非空、编号值唯一、参数必须是实例参数不能是类型参数。

public sealed class DynamicCommand : IRevitDynamicCommand
{
    private const string DeviceNumberParamName = "设备编号";

    private class DeviceInfo
    {
        public int Id;
        public string Category;
        public string FamilyName;
        public string TypeName;
        public bool HasInstanceParam;
        public bool HasTypeParam;
        public string DeviceNumber;
        public bool HasValue;
    }

    public object Execute(Document document)
    {
        // 遍历所有族实例,筛有"设备编号"参数的(建筑运维的设备)
        var allInstances = new FilteredElementCollector(document)
            .WhereElementIsNotElementType()
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .ToList();

        var devices = new List<DeviceInfo>();
        foreach (var inst in allInstances)
        {
            var instParams = inst.GetParameters(DeviceNumberParamName);
            var typeParams = inst.Symbol == null
                ? new List<Parameter>()
                : new List<Parameter>(inst.Symbol.GetParameters(DeviceNumberParamName));
            var hasInstParam = instParams.Count > 0;
            var hasTypeParam = typeParams.Count > 0;
            // 只处理有"设备编号"参数的(实例或类型)
            if (!hasInstParam && !hasTypeParam) continue;

            // 取参数值(优先实例参数的值)
            string numberValue = null;
            Parameter usedParam = null;
            if (hasInstParam)
            {
                numberValue = instParams[0].AsString();
                usedParam = instParams[0];
            }
            else if (hasTypeParam)
            {
                numberValue = typeParams[0].AsString();
                usedParam = typeParams[0];
            }
            if (string.IsNullOrWhiteSpace(numberValue) && usedParam != null)
                numberValue = usedParam.AsValueString();

            var category = inst.Category == null ? string.Empty : inst.Category.Name;
            var familyName = inst.Symbol == null ? string.Empty : inst.Symbol.Family.Name;
            var typeName = inst.Symbol == null ? string.Empty : inst.Symbol.Name;

            devices.Add(new DeviceInfo
            {
                Id = inst.Id.IntegerValue,
                Category = category,
                FamilyName = familyName,
                TypeName = typeName,
                HasInstanceParam = hasInstParam,
                HasTypeParam = hasTypeParam,
                DeviceNumber = numberValue ?? string.Empty,
                HasValue = !string.IsNullOrWhiteSpace(numberValue)
            });
        }

        // 编号值 → 设备数(查重)
        var numberGroups = devices
            .Where(d => d.HasValue)
            .GroupBy(d => d.DeviceNumber)
            .ToDictionary(g => g.Key, g => g.Count());
        var duplicateNumbers = new HashSet<string>(
            numberGroups.Where(kv => kv.Value > 1).Select(kv => kv.Key));

        // 判定每设备是否合格 + 收集 issues
        var issues = new List<object>();
        int compliant = 0;
        foreach (var d in devices)
        {
            var reasons = new List<string>();
            if (d.HasTypeParam) reasons.Add("'设备编号'是类型参数(应为实例参数)");
            if (!d.HasValue) reasons.Add("编号值为空");
            if (d.HasValue && duplicateNumbers.Contains(d.DeviceNumber)) reasons.Add("编号重复");

            if (reasons.Count == 0)
                compliant++;
            else
                issues.Add(new
                {
                    d.Id,
                    d.Category,
                    d.FamilyName,
                    d.TypeName,
                    d.HasInstanceParam,
                    d.HasTypeParam,
                    d.DeviceNumber,
                    Issue = string.Join("、", reasons)
                });
        }

        var duplicateGroups = numberGroups
            .Where(kv => kv.Value > 1)
            .OrderByDescending(kv => kv.Value)
            .Take(20)
            .Select(kv => new { Number = kv.Key, DeviceCount = kv.Value })
            .ToList();

        var duplicateDeviceCount = devices
            .Count(d => d.HasValue && duplicateNumbers.Contains(d.DeviceNumber));

        return new
        {
            DeviceCount = devices.Count,
            CompliantCount = compliant,
            NonCompliantCount = issues.Count,
            TypeParamCount = devices.Count(d => d.HasTypeParam),
            EmptyValueCount = devices.Count(d => !d.HasValue),
            DuplicateNumberCount = duplicateDeviceCount,
            DuplicateGroups = duplicateGroups,
            CategoryBreakdown = devices
                .GroupBy(d => d.Category)
                .Select(g => new { Category = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(20).ToList(),
            Issues = issues.Take(50).ToList()
        };
    }
}
