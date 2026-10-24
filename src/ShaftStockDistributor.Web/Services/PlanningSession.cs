using ShaftStockDistributor.Core;
using ShaftStockDistributor.Optimizer;
using ShaftStockDistributor.Simulation;

namespace ShaftStockDistributor.Web.Services;

/// <summary>
/// In-memory plan for one browser circuit. The optimizer keeps pinned shafts and rebuilds the rest
/// unless <see cref="Optimize"/> is asked to keep the current placement.
/// </summary>
public sealed class PlanningSession
{
    private readonly object _gate = new();

    public PlanningSession()
    {
        LoadExample();
    }

    public Layout Layout { get; private set; } = SampleWarehouse.CreateLayout();

    public ArticleSet Articles { get; private set; } = SampleWarehouse.CreateArticles();

    public Assignment Assignment { get; private set; } = new();

    public PlanningParameters Parameters { get; private set; } = SampleWarehouse.CreateParameters();

    public OptimizationResult? LastOptimization { get; private set; }

    public SimulationResult? LastSimulation { get; private set; }

    public string Status { get; private set; } = "";

    public void LoadExample()
    {
        lock (_gate)
        {
            Layout = SampleWarehouse.CreateLayout();
            Articles = SampleWarehouse.CreateArticles();
            Parameters = SampleWarehouse.CreateParameters();
            Assignment = ConstructiveAssigner.Build(Layout, Articles, Parameters);
            Assignment.ExampleData = true;
            Assignment.Notes = "Example assignment from the constructive heuristic, before annealing. Not a measured plan.";
            LastOptimization = null;
            LastSimulation = null;
            Status = "Loaded example data. Crate types and rates are assumptions, not warehouse measurements.";
        }
    }

    public void Optimize(bool keepCurrent)
    {
        lock (_gate)
        {
            LastOptimization = AnnealingOptimizer.Optimize(
                Layout,
                Articles,
                Parameters,
                Assignment,
                keepCurrent ? OptimizationStart.KeepCurrent : OptimizationStart.Constructive);
            Assignment = LastOptimization.Assignment;
            LastSimulation = null;
            Status = keepCurrent
                ? "Optimized from the current placement. Pinned shafts stayed put."
                : "Optimized. Unpinned shafts were rebuilt; pinned shafts stayed put.";
        }
    }

    public void Simulate(double hours, int seed)
    {
        lock (_gate)
        {
            LastSimulation = WarehouseSimulator.Run(
                Layout,
                Articles,
                Assignment,
                new SimulationOptions { Hours = hours, Seed = seed, RecordTrace = false });
            Status = LastSimulation.CollisionViolations == 0
                ? "Simulation finished."
                : "Simulation finished and recorded a shuttle collision. That is a defect in this prototype.";
        }
    }

    public bool AssignShaft(ShaftLocation location, string? articleId, bool pinned)
    {
        lock (_gate)
        {
            if (Layout.Find(location) is null)
            {
                Status = $"{location} is not in the layout.";
                return false;
            }

            if (Assignment.TryGet(location, out var existing) && existing.Pinned && existing.ArticleId != articleId)
            {
                Status = $"{location} is pinned.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(articleId))
            {
                if (existing?.Pinned == true)
                {
                    Status = $"{location} is pinned.";
                    return false;
                }

                Assignment.Remove(location);
            }
            else
            {
                if (Articles.Find(articleId) is null)
                {
                    Status = $"Unknown article '{articleId}'.";
                    return false;
                }

                Assignment.Set(location, articleId, pinned);
            }

            LastSimulation = null;
            Status = $"Updated {location}.";
            return true;
        }
    }

    public bool MoveOrSwap(ShaftLocation from, ShaftLocation to)
    {
        lock (_gate)
        {
            if (from == to || !Assignment.TryGet(from, out var source))
            {
                return false;
            }

            if (source.Pinned)
            {
                Status = $"{from} is pinned.";
                return false;
            }

            if (Layout.Find(to) is null)
            {
                return false;
            }

            if (Assignment.TryGet(to, out var target))
            {
                if (target.Pinned)
                {
                    Status = $"{to} is pinned.";
                    return false;
                }

                Assignment.Set(from, target.ArticleId, pinned: false);
                Assignment.Set(to, source.ArticleId, pinned: false);
                Status = $"Swapped {from} and {to}.";
            }
            else
            {
                Assignment.Remove(from);
                Assignment.Set(to, source.ArticleId, pinned: false);
                Status = $"Moved {source.ArticleId} from {from} to {to}.";
            }

            LastSimulation = null;
            return true;
        }
    }

    public void SetPinned(ShaftLocation location, bool pinned)
    {
        lock (_gate)
        {
            if (!Assignment.TryGet(location, out var assignment))
            {
                Status = "Pin an assigned shaft.";
                return;
            }

            Assignment.Set(location, assignment.ArticleId, pinned);
            Status = pinned ? $"Pinned {location}." : $"Unpinned {location}.";
        }
    }

    public Article AddArticle()
    {
        lock (_gate)
        {
            var number = 1;
            while (Articles.Items.Any(article => article.Id == $"NEW-{number}"))
            {
                number++;
            }

            var article = new Article
            {
                Id = $"NEW-{number}",
                Name = "New crate type",
                AbcClass = AbcClass.B,
                CratesPerHour = 20,
                CratesPerPallet = Articles.DefaultCratesPerPallet,
                Offset = Articles.DefaultOffset,
                ShaftCapacity = Layout.DefaultShaftCapacity
            };
            Articles.Items.Add(article);
            Status = $"Added {article.Id}. Assign it a shaft or run the optimizer.";
            return article;
        }
    }

    public void RemoveArticle(string id)
    {
        lock (_gate)
        {
            Articles.Items.RemoveAll(article => article.Id == id);
            foreach (var location in Assignment.Shafts.Where(pair => pair.Value.ArticleId == id).Select(pair => pair.Key).ToList())
            {
                Assignment.Remove(location);
            }

            LastSimulation = null;
            Status = $"Removed {id}.";
        }
    }

    public void Rename(Article article, string newId)
    {
        lock (_gate)
        {
            newId = newId.Trim();
            var oldId = article.Id;
            if (string.IsNullOrWhiteSpace(newId) || newId == oldId)
            {
                return;
            }

            if (Articles.Items.Any(other => !ReferenceEquals(other, article) && other.Id == newId))
            {
                Status = $"Article id '{newId}' already exists.";
                return;
            }

            article.Id = newId;
            foreach (var location in Assignment.Shafts.Where(pair => pair.Value.ArticleId == oldId).Select(pair => pair.Key).ToList())
            {
                Assignment.Set(location, newId, Assignment.Shafts[location].Pinned);
            }

            Status = $"Renamed {oldId} to {newId}.";
        }
    }

    public void ApplyGeometry(int segmentCount, int levels, int shaftsPerLevel, int defaultCapacity)
    {
        lock (_gate)
        {
            var document = WarehouseMapper.ToDocument(Layout);
            document.SegmentCount = segmentCount;
            document.LevelsPerSegment = levels;
            document.ShaftsPerLevel = shaftsPerLevel;
            document.DefaultShaftCapacity = defaultCapacity;
            document.Shuttle1Home = new ShuttleHomeDocument { Segment = 1, Level = 1, Shaft = 1 };
            document.Shuttle2Home = new ShuttleHomeDocument { Segment = Math.Max(1, segmentCount), Level = 1, Shaft = Math.Max(1, shaftsPerLevel) };
            document.ShaftCapacityOverrides.Clear();
            document.ExampleData = false;
            Layout = WarehouseMapper.ToLayout(document);
            Assignment = new Assignment();
            LastOptimization = null;
            LastSimulation = null;
            Status = "Rebuilt the rack and cleared the assignment. Shuttle homes are the outer ends at level 1.";
        }
    }

    public void ReplaceLayout(string json)
    {
        lock (_gate)
        {
            Layout = WarehouseFiles.ParseLayout(json);
            Assignment = new Assignment();
            LastOptimization = null;
            LastSimulation = null;
            Status = "Loaded a layout and cleared the assignment.";
        }
    }

    public void ReplaceArticles(string json)
    {
        lock (_gate)
        {
            Articles = WarehouseFiles.ParseArticles(json);
            LastSimulation = null;
            Status = "Loaded articles.";
        }
    }

    public void ReplaceAssignment(string json)
    {
        lock (_gate)
        {
            Assignment = WarehouseFiles.ParseAssignment(json);
            LastOptimization = null;
            LastSimulation = null;
            Status = "Loaded an assignment.";
        }
    }

    public void ReplaceParameters(string json)
    {
        lock (_gate)
        {
            Parameters = WarehouseFiles.ParseParameters(json);
            Status = "Loaded planner parameters.";
        }
    }

    public string AssignmentJson()
    {
        lock (_gate)
        {
            return WarehouseFiles.ToJson(Assignment);
        }
    }

    public IReadOnlyList<ValidationMessage> Validate()
    {
        lock (_gate)
        {
            return WarehouseValidator.Validate(Layout, Articles, Assignment);
        }
    }

    public IReadOnlyDictionary<string, double> Inflows()
    {
        lock (_gate)
        {
            return InflowResolver.Resolve(Articles);
        }
    }

    public IReadOnlyDictionary<string, int> Demand()
    {
        lock (_gate)
        {
            return ShaftDemandCalculator.ForArticles(Layout, Articles, InflowResolver.Resolve(Articles), Parameters);
        }
    }
}
