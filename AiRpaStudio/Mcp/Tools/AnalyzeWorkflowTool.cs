using System;
using System.Activities.Presentation.Model;
using System.Activities.Presentation.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using RPA.Core;

namespace RPA.Mcp.Tools
{
    /// <summary>
    /// Tool for analyzing workflow complexity and potential issues
    /// </summary>
    public class AnalyzeWorkflowTool : BaseMcpTool
    {
        private readonly CustomWfDesigner _designer;

        public AnalyzeWorkflowTool(CustomWfDesigner designer)
        {
            _designer = designer;
        }

        public override McpTool GetSchema()
        {
            return new McpTool
            {
                Name = "analyze_workflow",
                Description = "Analyzes the workflow for complexity, potential issues, code smells, and best practice violations",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        includeComplexity = new
                        {
                            type = "boolean",
                            description = "Include complexity metrics",
                            @default = true
                        },
                        includeIssues = new
                        {
                            type = "boolean",
                            description = "Include potential issues and warnings",
                            @default = true
                        },
                        includeSuggestions = new
                        {
                            type = "boolean",
                            description = "Include improvement suggestions",
                            @default = true
                        }
                    }
                }
            };
        }

        public override async Task<McpToolResult> Execute(object arguments)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (_designer.ActiveWorkflowDesignerEx == null)
                    {
                        return Error("No active workflow designer. Please open or create a workflow first.");
                    }

                    ModelService modelService = _designer.ActiveWorkflowDesignerEx.Context.Services.GetService<ModelService>();
                    if (modelService == null)
                    {
                        return Error("Failed to get ModelService");
                    }

                    // Analyze the workflow
                    WorkflowAnalysis analysis = AnalyzeWorkflowTree(modelService.Root);

                    List<object> issues = new List<object>();
                    List<object> suggestions = new List<object>();

                    // Check for potential issues
                    if (analysis.MaxDepth > 5)
                    {
                        issues.Add(new
                        {
                            severity = "warning",
                            type = "high_nesting",
                            message = $"Deep nesting detected (depth: {analysis.MaxDepth}). Consider refactoring to separate workflows.",
                            metric = analysis.MaxDepth
                        });
                    }

                    if (analysis.TotalActivities > 50)
                    {
                        suggestions.Add(new
                        {
                            type = "size",
                            message = $"Large workflow with {analysis.TotalActivities} activities. Consider breaking into smaller workflows.",
                            metric = analysis.TotalActivities
                        });
                    }

                    if (analysis.TryCatchCount == 0 && analysis.TotalActivities > 10)
                    {
                        suggestions.Add(new
                        {
                            type = "error_handling",
                            message = "No error handling detected. Consider adding TryCatch activities for robustness."
                        });
                    }

                    // Calculate complexity score (simple heuristic)
                    int complexityScore = analysis.TotalActivities + (analysis.MaxDepth * 5) + (analysis.DecisionPoints * 3);

                    return Success(new
                    {
                        message = "Workflow analysis completed",
                        complexity = new
                        {
                            score = complexityScore,
                            level = complexityScore < 20 ? "low" : complexityScore < 50 ? "medium" : "high",
                            totalActivities = analysis.TotalActivities,
                            maxDepth = analysis.MaxDepth,
                            decisionPoints = analysis.DecisionPoints
                        },
                        statistics = new
                        {
                            sequences = analysis.SequenceCount,
                            conditionals = analysis.ConditionalCount,
                            loops = analysis.LoopCount,
                            tryCatch = analysis.TryCatchCount
                        },
                        issues,
                        suggestions
                    });
                }
                catch (Exception ex)
                {
                    return Error($"Failed to analyze workflow: {ex.Message}");
                }
            });
        }

        private class WorkflowAnalysis
        {
            public int TotalActivities { get; set; }
            public int MaxDepth { get; set; }
            public int DecisionPoints { get; set; }
            public int SequenceCount { get; set; }
            public int ConditionalCount { get; set; }
            public int LoopCount { get; set; }
            public int TryCatchCount { get; set; }
        }

        private WorkflowAnalysis AnalyzeWorkflowTree(ModelItem root, int depth = 0)
        {
            WorkflowAnalysis analysis = new WorkflowAnalysis
            {
                TotalActivities = 0,
                MaxDepth = depth,
                DecisionPoints = 0,
                SequenceCount = 0,
                ConditionalCount = 0,
                LoopCount = 0,
                TryCatchCount = 0
            };

            if (root == null)
            {
                return analysis;
            }

            analysis.TotalActivities = 1;

            // Count activity types
            string typeName = root.ItemType?.Name;
            switch (typeName)
            {
                case "Sequence":
                    analysis.SequenceCount = 1;
                    break;
                case "If":
                case "Switch":
                case "FlowDecision":
                    analysis.ConditionalCount = 1;
                    analysis.DecisionPoints = 1;
                    break;
                case "While":
                case "DoWhile":
                case "ForEach":
                    analysis.LoopCount = 1;
                    break;
                case "TryCatch":
                    analysis.TryCatchCount = 1;
                    break;
                default:
                    break;
            }

            // Recursively analyze children
            foreach (ModelProperty property in root.Properties)
            {
                if (property.Value != null)
                {
                    WorkflowAnalysis childAnalysis = AnalyzeWorkflowTree(property.Value, depth + 1);
                    MergeAnalysis(analysis, childAnalysis);
                }

                if (property.Collection != null)
                {
                    foreach (ModelItem item in property.Collection)
                    {
                        WorkflowAnalysis childAnalysis = AnalyzeWorkflowTree(item, depth + 1);
                        MergeAnalysis(analysis, childAnalysis);
                    }
                }
            }

            return analysis;
        }

        private void MergeAnalysis(WorkflowAnalysis target, WorkflowAnalysis source)
        {
            target.TotalActivities += source.TotalActivities;
            target.MaxDepth = Math.Max(target.MaxDepth, source.MaxDepth);
            target.DecisionPoints += source.DecisionPoints;
            target.SequenceCount += source.SequenceCount;
            target.ConditionalCount += source.ConditionalCount;
            target.LoopCount += source.LoopCount;
            target.TryCatchCount += source.TryCatchCount;
        }
    }
}
