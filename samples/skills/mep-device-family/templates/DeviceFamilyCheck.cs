using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitAgent.DynamicCode;

// mep-device-family 技能模板:机电设备族类型合规检查。
// 检查项:设备必须用族建(FamilyInstance),不能用体量(Mass 族类别)建。

public sealed class DynamicCommand : IRevitDynamicCommand
{
    private const string DeviceNumberParamName = "设备编号";

    private class DeviceInfo
    {
        public int Id;
        public string Category;
        public string FamilyName;
        public string FamilyCategory;
        public bool IsMassFamily;
        public string DeviceNumber;
    }

    public object Execute(Document document)
    {
        // 筛选有"设备编号"参数的族实例
        var devices = new FilteredElementCollector(document)
            .WhereElementIsNotElementType()
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .Where(inst => inst.GetParameters(DeviceNumberParamName).Count > 0
                        || (inst.Symbol != null && inst.Symbol.GetParameters(DeviceNumberParamName).Count > 0))
            .Select(inst =>
            {
                var family = inst.Symbol == null ? null : inst.Symbol.Family;
                var familyCat = family == null ? null : family.FamilyCategory;
                // 2019 RevitAPI 的 Category 无 BuiltInCategory 属性;用族类别 ElementId 与枚举值比较(全版本通用)。
                var isMass = familyCat != null && familyCat.Id.IntegerValue == (int)BuiltInCategory.OST_Mass;

                var numberParams = inst.GetParameters(DeviceNumberParamName);
                string numberValue = null;
                if (numberParams.Count > 0)
                {
                    numberValue = numberParams[0].AsString();
                    if (string.IsNullOrWhiteSpace(numberValue))
                        numberValue = numberParams[0].AsValueString();
                }

                return new DeviceInfo
                {
                    Id = inst.Id.IntegerValue,
                    Category = inst.Category == null ? string.Empty : inst.Category.Name,
                    FamilyName = family == null ? string.Empty : family.Name,
                    FamilyCategory = familyCat == null ? string.Empty : familyCat.Name,
                    IsMassFamily = isMass,
                    DeviceNumber = numberValue ?? string.Empty
                };
            })
            .ToList();

        // 判定 + issues
        var issues = new List<object>();
        int compliant = 0;
        foreach (var d in devices)
        {
            var reasons = new List<string>();
            if (d.IsMassFamily) reasons.Add("用体量(Mass)建,应改用族");
            if (reasons.Count == 0)
                compliant++;
            else
                issues.Add(new
                {
                    d.Id,
                    d.Category,
                    d.FamilyName,
                    d.FamilyCategory,
                    d.IsMassFamily,
                    d.DeviceNumber,
                    Issue = string.Join("、", reasons)
                });
        }

        return new
        {
            DeviceCount = devices.Count,
            CompliantCount = compliant,
            NonCompliantCount = issues.Count,
            MassFamilyCount = devices.Count(d => d.IsMassFamily),
            CategoryBreakdown = devices
                .GroupBy(d => d.FamilyCategory)
                .Select(g => new { FamilyCategory = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(20).ToList(),
            Issues = issues.Take(50).ToList()
        };
    }
}
