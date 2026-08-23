using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitAgent.DynamicCode;

// 模型概览：针对"二结构墙体 + 构造柱"模型，汇总标高/房间/墙/柱（含结构柱与建筑柱两类）。
// 构造柱可能按 OST_StructuralColumns 或 OST_Columns 建模，两类都统计以免遗漏。
public sealed class DynamicCommand : IRevitDynamicCommand
{
    public object Execute(Document document)
    {
        // 标高（按标高排序，内部单位 feet→mm）
        var levels = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Levels)
            .WhereElementIsNotElementType()
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .Select(l => new
            {
                Id = l.Id.IntegerValue,
                l.Name,
                ElevationMm = System.Math.Round(l.Elevation * 304.8, 0)
            })
            .ToList();

        // 房间
        var rooms = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Cast<Room>()
            .ToList();
        var placedRooms = rooms.Where(r => r.Location != null).ToList();

        // 墙
        var walls = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Walls)
            .WhereElementIsNotElementType()
            .Cast<Wall>()
            .ToList();
        var wallTypes = walls
            .GroupBy(w => w.WallType?.Name ?? "(无类型)")
            .Select(g => new { TypeName = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToList();

        // 结构柱（OST_StructuralColumns）
        var structCols = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_StructuralColumns)
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .ToList();

        // 建筑柱（OST_Columns）——构造柱有时按此类建模
        var archCols = new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_Columns)
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .ToList();

        var colSymbols = structCols.Concat(archCols)
            .GroupBy(c => c.Symbol?.Name ?? "(无符号)")
            .Select(g => new { Symbol = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToList();

        return new
        {
            LevelCount = levels.Count,
            Levels = levels,
            RoomCount = rooms.Count,
            PlacedRoomCount = placedRooms.Count,
            WallCount = walls.Count,
            WallTypeCount = wallTypes.Count,
            WallTypes = wallTypes,
            StructuralColumnCount = structCols.Count,
            ArchitecturalColumnCount = archCols.Count,
            ColumnSymbolCount = colSymbols.Count,
            ColumnSymbols = colSymbols
        };
    }
}
