using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RPA.Chat.Services.Spec;
using RPA.Chat.Services.Workflow;

namespace RPA.Chat.Services.AgentTools
{
    /// <summary>
    /// Обработчики инструментов ТЗ (волна 8, <c>TZ-01</c>…<c>TZ-09</c>): <c>spec_import</c>, <c>spec_show</c>,
    /// <c>spec_extract</c>, <c>spec_update</c>, <c>spec_decompose</c>, <c>spec_link</c>, <c>spec_coverage</c>,
    /// <c>spec_generate</c>, <c>spec_ask</c>, <c>spec_answer</c>, <c>spec_testcases</c>, <c>spec_run_tests</c>.
    /// <para>
    /// Логика вынесена из <see cref="AgentToolFactory"/>, чтобы реестр оставался читаемым. Все ответы —
    /// текст с адресами: «не выполнено» и «ошибок нет» — разные фразы, «не запускался» не выглядит как «пройден».
    /// </para>
    /// </summary>
    internal sealed class SpecToolHandlers
    {
        private const int DefaultShowChars = SpecificationText.DefaultShowChars;

        private readonly Func<string> _getProjectDir;

        public SpecToolHandlers(Func<string> getProjectDir)
        {
            _getProjectDir = getProjectDir ?? (() => string.Empty);
        }

        // ------------------------------------------------------------------
        // Хранилище и модель
        // ------------------------------------------------------------------

        /// <summary><c>spec_import</c> (<c>TZ-01</c>): принять ТЗ из файла или текста, создать версию, показать дифф.</summary>
        public Task<string> ImportAsync(JObject args, CancellationToken cancellationToken)
        {
            var request = new SpecImportRequest
            {
                Path = ArgText(args, "path"),
                Text = args.Value<string>("text"),
                Name = ArgText(args, "name"),
                Slug = ArgText(args, "slug"),
                Note = ArgText(args, "note")
            };

            if (string.IsNullOrWhiteSpace(request.Path) && string.IsNullOrWhiteSpace(request.Text))
            {
                return Task.FromResult("spec_import: укажите path к файлу ТЗ (md, txt, docx, pdf, xlsx) или text");
            }

            if (!string.IsNullOrWhiteSpace(request.Path) && !string.IsNullOrWhiteSpace(request.Text))
            {
                return Task.FromResult("spec_import: передайте либо path, либо text — не оба сразу");
            }

            var store = Store();
            var result = store.Import(request);
            if (!result.Ok)
            {
                return Task.FromResult("spec_import: " + result.Describe());
            }

            var text = new StringBuilder(result.Describe());
            if (result.Outcome != SpecImportOutcome.Unchanged)
            {
                text.AppendLine().AppendLine(
                    "следующий шаг: spec_extract — извлеките модель (цель, шаги, данные, критерии) из текста ТЗ " +
                    "и передайте JSON; инструмент проверит и сохранит её.");
            }

            return Task.FromResult(text.ToString().TrimEnd());
        }

        /// <summary><c>spec_show</c> (<c>TZ-04</c>): модель ТЗ или текст текущей версии по разделам.</summary>
        public Task<string> ShowAsync(JObject args, CancellationToken cancellationToken)
        {
            string slug = ArgText(args, "slug");
            string section = ArgText(args, "section");
            int maxChars = ArgInt(args, "max_chars") ?? DefaultShowChars;

            var store = Store();
            var resolved = ResolveSlug(store, slug);
            if (resolved.Error != null)
            {
                return Task.FromResult("spec_show: " + resolved.Error);
            }

            if (store.TryLoad(resolved.Slug, out var specification, out string error))
            {
                var text = new StringBuilder();
                text.Append(SpecificationText.Show(specification, section, maxChars));
                text.AppendLine().AppendLine();
                text.Append("проверка модели: ")
                    .Append(SpecificationValidator.Validate(specification).Describe());
                return Task.FromResult(text.ToString().TrimEnd());
            }

            if (store.TryReadCurrentText(resolved.Slug, out string raw, out string textError))
            {
                var text = new StringBuilder();
                text.Append(SpecificationText.ShowText(resolved.Slug, raw, section, maxChars));
                text.AppendLine().AppendLine();
                text.Append("проверка модели: модель ТЗ не собрана — ").Append(error);
                return Task.FromResult(text.ToString().TrimEnd());
            }

            return Task.FromResult("spec_show: " + textError);
        }

        /// <summary><c>spec_extract</c> (<c>TZ-03</c>): принять JSON модели от агента, проверить и сохранить.</summary>
        public Task<string> ExtractAsync(JObject args, CancellationToken cancellationToken)
        {
            string slug = ArgText(args, "slug");
            string json = ModelJson(args);
            if (string.IsNullOrWhiteSpace(json))
            {
                return Task.FromResult("spec_extract: передайте модель в model (объект) или json (строка)");
            }

            var store = Store();
            var resolved = ResolveSlug(store, slug, allowEmpty: true, fallbackName: ArgText(args, "title"));
            if (resolved.Error != null)
            {
                return Task.FromResult("spec_extract: " + resolved.Error);
            }

            RpaSpecification specification;
            try
            {
                specification = RpaSpecification.FromJson(json);
            }
            catch (Exception ex)
            {
                return Task.FromResult("spec_extract: JSON модели не разобран — " + ex.Message +
                                       ". Ожидается объект с полями goal, steps, data, acceptanceCriteria.");
            }

            if (string.IsNullOrWhiteSpace(specification.Slug))
            {
                specification.Slug = resolved.Slug;
            }

            if (string.IsNullOrWhiteSpace(specification.Title) && !string.IsNullOrWhiteSpace(ArgText(args, "title")))
            {
                specification.Title = ArgText(args, "title");
            }

            AttachSource(store, specification, resolved.Slug);

            var report = SpecificationValidator.Validate(specification);
            if (report.BlockingCount > 0 && !ArgBool(args, "force"))
            {
                return Task.FromResult("spec_extract: модель не сохранена — " + report.Describe() +
                                       Environment.NewLine +
                                       "исправьте замечания и повторите; force=true сохранит модель как черновик.");
            }

            var saved = store.Save(specification);
            if (!saved.Ok)
            {
                return Task.FromResult("spec_extract: " + saved.Error);
            }

            var text = new StringBuilder();
            text.Append("модель ТЗ сохранена: шагов ").Append(specification.Steps.Count)
                .Append(", данных ").Append(specification.Data.Count)
                .Append(", критериев ").Append(specification.AcceptanceCriteria.Count).AppendLine();
            text.Append("JSON: ").AppendLine(saved.JsonPath);
            text.Append("markdown: ").AppendLine(saved.MarkdownPath);
            text.Append("проверка: ").Append(report.Describe());
            return Task.FromResult(text.ToString().TrimEnd());
        }

        /// <summary><c>spec_update</c> (<c>TZ-04</c>): правка частей модели (patch) без переписывания целиком.</summary>
        public Task<string> UpdateAsync(JObject args, CancellationToken cancellationToken)
        {
            string patchJson = ModelJson(args);
            if (string.IsNullOrWhiteSpace(patchJson))
            {
                return Task.FromResult("spec_update: передайте правку в model (объект) или json (строка)");
            }

            var store = Store();
            var resolved = ResolveSlug(store, ArgText(args, "slug"));
            if (resolved.Error != null)
            {
                return Task.FromResult("spec_update: " + resolved.Error);
            }

            if (!store.TryLoad(resolved.Slug, out var specification, out string error))
            {
                return Task.FromResult("spec_update: " + error);
            }

            JObject patch;
            try
            {
                patch = JObject.Parse(patchJson);
            }
            catch (Exception ex)
            {
                return Task.FromResult("spec_update: правка не разобрана — " + ex.Message);
            }

            var current = JObject.Parse(specification.ToJson());
            current.Merge(patch, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });

            RpaSpecification updated;
            try
            {
                updated = RpaSpecification.FromJson(current.ToString());
            }
            catch (Exception ex)
            {
                return Task.FromResult("spec_update: после правки модель не разбирается — " + ex.Message);
            }

            if (string.IsNullOrWhiteSpace(updated.Slug))
            {
                updated.Slug = resolved.Slug;
            }

            var report = SpecificationValidator.Validate(updated);
            if (report.BlockingCount > 0 && !ArgBool(args, "force"))
            {
                return Task.FromResult("spec_update: правка не сохранена — " + report.Describe() +
                                       Environment.NewLine + "force=true сохранит модель как черновик.");
            }

            var saved = store.Save(updated);
            if (!saved.Ok)
            {
                return Task.FromResult("spec_update: " + saved.Error);
            }

            return Task.FromResult("spec_update: правка применена (поля: " +
                                   string.Join(", ", patch.Properties().Select(property => property.Name)) + ")" +
                                   Environment.NewLine + "проверка: " + report.Describe());
        }

        // ------------------------------------------------------------------
        // Декомпозиция (TZ-05)
        // ------------------------------------------------------------------

        /// <summary><c>spec_decompose</c> (<c>TZ-05</c>): сохранить декомпозицию и проверить покрытие пунктов.</summary>
        public Task<string> DecomposeAsync(JObject args, CancellationToken cancellationToken)
        {
            var store = Store();
            var resolved = ResolveSlug(store, ArgText(args, "slug"));
            if (resolved.Error != null)
            {
                return Task.FromResult("spec_decompose: " + resolved.Error);
            }

            if (!store.TryLoad(resolved.Slug, out var specification, out string error))
            {
                return Task.FromResult("spec_decompose: " + error);
            }

            string json = ModelJson(args);
            List<SpecTask> tasks;
            try
            {
                tasks = args["tasks"] is JArray direct
                    ? JsonConvert.DeserializeObject<List<SpecTask>>(direct.ToString(), RpaSpecification.JsonSettings())
                    : json != null
                    ? JsonConvert.DeserializeObject<List<SpecTask>>(json, RpaSpecification.JsonSettings())
                    : null;
                if (tasks == null)
                {
                    var container = args["tasks"] as JObject ?? args["model"] as JObject;
                    if (container?["tasks"] is JArray nested)
                    {
                        tasks = JsonConvert.DeserializeObject<List<SpecTask>>(nested.ToString(),
                            RpaSpecification.JsonSettings());
                    }
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult("spec_decompose: декомпозиция не разобрана — " + ex.Message);
            }

            if (tasks == null || tasks.Count == 0)
            {
                return Task.FromResult("spec_decompose: передайте задачи в tasks (массив) или json");
            }

            specification.Tasks = tasks;
            specification.Normalize();

            var report = SpecificationValidator.ValidateDecomposition(specification);
            if (report.BlockingCount > 0 && !ArgBool(args, "force"))
            {
                return Task.FromResult("spec_decompose: декомпозиция не сохранена — " + report.Describe() +
                                       Environment.NewLine + "force=true сохранит как черновик.");
            }

            var saved = store.Save(specification);
            if (!saved.Ok)
            {
                return Task.FromResult("spec_decompose: " + saved.Error);
            }

            var text = new StringBuilder();
            text.Append("декомпозиция сохранена: задач ").Append(specification.Tasks.Count).AppendLine();
            text.Append("покрытие: ").Append(report.Describe()).AppendLine();
            text.Append("следующий шаг: spec_generate (план и применение) после spec_link/spec_coverage.").AppendLine();
            text.Append(SpecificationText.Show(specification, "tasks", DefaultShowChars));
            return Task.FromResult(text.ToString().TrimEnd());
        }

        // ------------------------------------------------------------------
        // Трассировка (TZ-06)
        // ------------------------------------------------------------------

        /// <summary><c>spec_link</c> (<c>TZ-06</c>): связать пункт ТЗ с элементом workflow.</summary>
        public Task<string> LinkAsync(JObject args, CancellationToken cancellationToken)
        {
            string itemId = ArgText(args, "itemId") ?? ArgText(args, "item");
            string elementId = ArgText(args, "elementId") ?? ArgText(args, "element");
            if (string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(elementId))
            {
                return Task.FromResult("spec_link: нужны itemId (пункт ТЗ) и elementId (адрес элемента workflow)");
            }

            var store = Store();
            var resolved = ResolveSlug(store, ArgText(args, "slug"));
            if (resolved.Error != null)
            {
                return Task.FromResult("spec_link: " + resolved.Error);
            }

            if (!store.TryLoad(resolved.Slug, out var specification, out string error))
            {
                return Task.FromResult("spec_link: " + error);
            }

            var known = specification.AllItemIds();
            if (!known.Any(id => string.Equals(id, itemId, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult("spec_link: пункта «" + itemId + "» нет в ТЗ. Известные пункты: " +
                                       (known.Count == 0 ? "(нет)" : string.Join(", ", known)));
            }

            var outline = Outline(ArgText(args, "path"));
            if (outline != null && outline.Root != null)
            {
                bool exists = outline.All().Any(node =>
                    string.Equals(node.Id, elementId, StringComparison.OrdinalIgnoreCase));
                if (!exists)
                {
                    return Task.FromResult("spec_link: элемента «" + elementId + "» нет в структуре процесса. " +
                                           "Прочитайте адреса через workflow_outline.");
                }
            }
            else
            {
                return Task.FromResult("spec_link: структура процесса не прочитана (укажите path к XAML " +
                                       "или откройте его в конструкторе) — метка не поставлена");
            }

            specification.Links.RemoveAll(link =>
                string.Equals((link.ItemId ?? string.Empty).Trim(), itemId.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals((link.ElementId ?? string.Empty).Trim(), elementId.Trim(), StringComparison.OrdinalIgnoreCase));

            specification.Links.Add(new SpecLink
            {
                ItemId = itemId.Trim(),
                ElementId = elementId.Trim(),
                Note = ArgText(args, "note") ?? string.Empty,
                CreatedAtUtc = DateTime.UtcNow.ToString("u")
            });

            var saved = store.Save(specification);
            if (!saved.Ok)
            {
                return Task.FromResult("spec_link: " + saved.Error);
            }

            var coverage = SpecCoverage.Build(specification, outline);
            return Task.FromResult("spec_link: метка " + itemId + " → " + elementId + " сохранена" +
                                   Environment.NewLine + coverage.Describe());
        }

        /// <summary><c>spec_coverage</c> (<c>TZ-06</c>): отчёт о покрытии пунктов ТЗ элементами workflow.</summary>
        public Task<string> CoverageAsync(JObject args, CancellationToken cancellationToken)
        {
            var store = Store();
            var resolved = ResolveSlug(store, ArgText(args, "slug"));
            if (resolved.Error != null)
            {
                return Task.FromResult("spec_coverage: " + resolved.Error);
            }

            if (!store.TryLoad(resolved.Slug, out var specification, out string error))
            {
                return Task.FromResult("spec_coverage: " + error);
            }

            var outline = Outline(ArgText(args, "path"));
            var report = SpecCoverage.Build(specification, outline);
            return Task.FromResult(report.Describe());
        }

        // ------------------------------------------------------------------
        // Генерация (TZ-07)
        // ------------------------------------------------------------------

        /// <summary><c>spec_generate</c> (<c>TZ-07</c>): план генерации и (по apply=true) его применение пачками.</summary>
        public Task<string> GenerateAsync(JObject args, CancellationToken cancellationToken)
        {
            var store = Store();
            var resolved = ResolveSlug(store, ArgText(args, "slug"));
            if (resolved.Error != null)
            {
                return Task.FromResult("spec_generate: " + resolved.Error);
            }

            if (!store.TryLoad(resolved.Slug, out var specification, out string error))
            {
                return Task.FromResult("spec_generate: " + error);
            }

            string path = ArgText(args, "path");
            var outline = Outline(path);
            var plan = SpecGenerator.BuildPlan(specification, outline, ChatEnvironment.ActivityCatalog, path);

            if (!ArgBool(args, "apply"))
            {
                return Task.FromResult("spec_generate: план построен, правки не применялись (apply=true применит)" +
                                       Environment.NewLine + plan.Describe());
            }

            if (outline == null || outline.Root == null)
            {
                return Task.FromResult("spec_generate: генерация не выполнена — структура процесса не прочитана " +
                                       "(укажите path к XAML или откройте его в конструкторе)");
            }

            var result = SpecGenerator.Apply(plan, ChatEnvironment.WorkflowEditing, path);

            if (result.Ok)
            {
                foreach (var pair in result.TaskElements)
                {
                    var task = specification.Tasks.FirstOrDefault(item =>
                        string.Equals(item.Id, pair.Key, StringComparison.OrdinalIgnoreCase));
                    if (task == null)
                    {
                        continue;
                    }

                    task.ElementIds = pair.Value.ToList();
                    task.Status = SpecTaskStatus.Done;

                    // Метка ставится только когда связь однозначна: один элемент и один пункт ТЗ.
                    if (pair.Value.Count == 1 && task.SpecRefs.Count == 1)
                    {
                        bool exists = specification.Links.Any(link =>
                            string.Equals(link.ItemId, task.SpecRefs[0], StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(link.ElementId, pair.Value[0], StringComparison.OrdinalIgnoreCase));
                        if (!exists)
                        {
                            specification.Links.Add(new SpecLink
                            {
                                ItemId = task.SpecRefs[0],
                                ElementId = pair.Value[0],
                                Note = "сгенерировано (TZ-07)",
                                CreatedAtUtc = DateTime.UtcNow.ToString("u")
                            });
                        }
                    }
                }

                store.Save(specification);
            }

            return Task.FromResult(result.Describe());
        }

        // ------------------------------------------------------------------
        // Журнал неоднозначностей (TZ-09)
        // ------------------------------------------------------------------

        /// <summary><c>spec_ask</c> (<c>TZ-09</c>): зафиксировать вопрос и варианты в журнале ТЗ.</summary>
        public Task<string> AskAsync(JObject args, CancellationToken cancellationToken)
        {
            string question = ArgText(args, "question");
            if (string.IsNullOrWhiteSpace(question))
            {
                return Task.FromResult("spec_ask: нужен question (текст вопроса)");
            }

            var store = Store();
            var resolved = ResolveSlug(store, ArgText(args, "slug"));
            if (resolved.Error != null)
            {
                return Task.FromResult("spec_ask: " + resolved.Error);
            }

            if (!store.TryLoad(resolved.Slug, out var specification, out string error))
            {
                return Task.FromResult("spec_ask: " + error);
            }

            var options = ArgStringList(args, "options");
            string id = ArgText(args, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                id = NextId("Q", specification.Ambiguities.Select(item => item.Id));
            }

            specification.Ambiguities.Add(new SpecAmbiguity
            {
                Id = id,
                ItemId = ArgText(args, "itemId") ?? string.Empty,
                Question = question,
                Options = options,
                Status = SpecAmbiguityStatus.Open
            });

            var saved = store.Save(specification);
            if (!saved.Ok)
            {
                return Task.FromResult("spec_ask: " + saved.Error);
            }

            var text = new StringBuilder();
            text.Append("вопрос ").Append(id).Append(" записан в журнал ТЗ (статус: открыт)").AppendLine();
            text.Append("чтобы пользователь увидел его в чате, задайте тот же вопрос через ask_question " +
                        "(2–5 вариантов).").AppendLine();
            text.Append(SpecificationText.Show(specification, "ambiguities", DefaultShowChars));
            return Task.FromResult(text.ToString().TrimEnd());
        }

        /// <summary><c>spec_answer</c> (<c>TZ-09</c>): записать ответ, закрыть вопрос и (если нужно) создать версию ТЗ.</summary>
        public Task<string> AnswerAsync(JObject args, CancellationToken cancellationToken)
        {
            string id = ArgText(args, "id");
            string answer = ArgText(args, "answer");
            if (string.IsNullOrWhiteSpace(id) || answer == null)
            {
                return Task.FromResult("spec_answer: нужны id вопроса и answer (ответ; пустая строка допустима)");
            }

            var store = Store();
            var resolved = ResolveSlug(store, ArgText(args, "slug"));
            if (resolved.Error != null)
            {
                return Task.FromResult("spec_answer: " + resolved.Error);
            }

            if (!store.TryLoad(resolved.Slug, out var specification, out string error))
            {
                return Task.FromResult("spec_answer: " + error);
            }

            var ambiguity = specification.FindAmbiguity(id);
            if (ambiguity == null)
            {
                return Task.FromResult("spec_answer: вопроса «" + id + "» нет в журнале. Есть: " +
                                       (specification.Ambiguities.Count == 0
                                           ? "(журнал пуст)"
                                           : string.Join(", ", specification.Ambiguities.Select(item => item.Id))));
            }

            ambiguity.Answer = answer;
            ambiguity.AnsweredBy = ArgText(args, "answered_by") ?? "пользователь";
            ambiguity.AnsweredAtUtc = DateTime.UtcNow.ToString("u");
            ambiguity.Status = SpecAmbiguityStatus.Resolved;

            var saved = store.Save(specification);
            if (!saved.Ok)
            {
                return Task.FromResult("spec_answer: " + saved.Error);
            }

            var text = new StringBuilder();
            text.Append("вопрос ").Append(id).Append(" закрыт: ").Append(answer).AppendLine();

            string revisedText = args.Value<string>("text");
            if (!string.IsNullOrWhiteSpace(revisedText))
            {
                var version = store.AppendVersion(resolved.Slug, revisedText,
                    "ответ на вопрос " + id + ": " + answer);
                text.AppendLine().Append(version.Ok
                    ? "новая версия ТЗ: " + version.Version + " (файл " + version.VersionPath + ")"
                    : "версия ТЗ не создана: " + version.Reason);
            }

            text.AppendLine().Append(SpecificationText.Show(specification, "ambiguities", DefaultShowChars));
            return Task.FromResult(text.ToString().TrimEnd());
        }

        // ------------------------------------------------------------------
        // Тест-кейсы и прогон (TZ-08)
        // ------------------------------------------------------------------

        /// <summary><c>spec_testcases</c> (<c>TZ-08</c>): сохранить тест-кейсы и проверить покрытие критериев.</summary>
        public Task<string> TestCasesAsync(JObject args, CancellationToken cancellationToken)
        {
            var store = Store();
            var resolved = ResolveSlug(store, ArgText(args, "slug"));
            if (resolved.Error != null)
            {
                return Task.FromResult("spec_testcases: " + resolved.Error);
            }

            if (!store.TryLoad(resolved.Slug, out var specification, out string error))
            {
                return Task.FromResult("spec_testcases: " + error);
            }

            string json = ModelJson(args);
            List<SpecTestCase> testCases;
            try
            {
                testCases = json != null
                    ? JsonConvert.DeserializeObject<List<SpecTestCase>>(json, RpaSpecification.JsonSettings())
                    : null;
                if (testCases == null && args["testCases"] is JArray array)
                {
                    testCases = JsonConvert.DeserializeObject<List<SpecTestCase>>(array.ToString(),
                        RpaSpecification.JsonSettings());
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult("spec_testcases: кейсы не разобраны — " + ex.Message);
            }

            if (testCases == null || testCases.Count == 0)
            {
                return Task.FromResult("spec_testcases: передайте кейсы в testCases (массив) или json");
            }

            specification.TestCases = testCases;
            specification.Normalize();

            var report = SpecificationValidator.ValidateTestCases(specification);
            if (report.BlockingCount > 0 && !ArgBool(args, "force"))
            {
                return Task.FromResult("spec_testcases: кейсы не сохранены — " + report.Describe() +
                                       Environment.NewLine + "force=true сохранит как черновик.");
            }

            var saved = store.Save(specification);
            if (!saved.Ok)
            {
                return Task.FromResult("spec_testcases: " + saved.Error);
            }

            return Task.FromResult("тест-кейсы сохранены: " + specification.TestCases.Count +
                                   Environment.NewLine + "проверка: " + report.Describe());
        }

        /// <summary>
        /// <c>spec_run_tests</c> (<c>TZ-08</c>): прогнать кейсы, получить чек-лист приёмки и записать отчёт в
        /// <c>docs\spec\&lt;slug&gt;\runs\</c>. «Не запускался» не выдаётся за «пройден».
        /// </summary>
        public Task<string> RunTestsAsync(JObject args, CancellationToken cancellationToken)
        {
            var store = Store();
            var resolved = ResolveSlug(store, ArgText(args, "slug"));
            if (resolved.Error != null)
            {
                return Task.FromResult("spec_run_tests: " + resolved.Error);
            }

            if (!store.TryLoad(resolved.Slug, out var specification, out string error))
            {
                return Task.FromResult("spec_run_tests: " + error);
            }

            string path = ArgText(args, "path");
            if (string.IsNullOrWhiteSpace(path))
            {
                return Task.FromResult("spec_run_tests: нужен path к XAML процесса, который прогоняется");
            }

            var report = SpecTestRunner.Run(specification, ChatEnvironment.WorkflowRun, path, resolved.Slug);

            var text = new StringBuilder();
            text.Append(report.Describe()).AppendLine();

            if (!ChatEnvironment.WorkflowRun.Available)
            {
                text.AppendLine().Append("прогон не выполнен: " + ChatEnvironment.WorkflowRun.UnavailableReason +
                                         " — чек-лист показывает «не запускался», а не «пройден».");
            }

            try
            {
                string runsDirectory = Path.Combine(store.GetSpecDirectory(resolved.Slug), SpecLayout.RunsFolderName);
                Directory.CreateDirectory(runsDirectory);
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                string jsonPath = Path.Combine(runsDirectory, stamp + ".json");
                string mdPath = Path.Combine(runsDirectory, stamp + ".md");
                File.WriteAllText(jsonPath, report.ToJson(), new UTF8Encoding(false));
                File.WriteAllText(mdPath, "# Прогон тест-кейсов " + resolved.Slug + Environment.NewLine + Environment.NewLine +
                                           report.Describe() + Environment.NewLine, new UTF8Encoding(false));
                text.AppendLine().Append("отчёт: ").Append(jsonPath);
            }
            catch (Exception ex)
            {
                text.AppendLine().Append("отчёт не записан: " + ex.Message);
            }

            return Task.FromResult(text.ToString().TrimEnd());
        }

        // ------------------------------------------------------------------
        // Помощники
        // ------------------------------------------------------------------

        private static ISpecificationStore Store()
        {
            return ChatEnvironment.SpecStore ?? new SpecificationStore(() => ChatEnvironment.SolutionPathProvider());
        }

        private WorkflowOutline Outline(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string resolvedPath = Path.IsPathRooted(path) ? path : Path.Combine(_getProjectDir(), path);
            try
            {
                return ChatEnvironment.WorkflowOutline.GetOutline(resolvedPath, 0, false);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void AttachSource(ISpecificationStore store, RpaSpecification specification, string slug)
        {
            try
            {
                string directory = store.GetSpecDirectory(slug);
                string metaPath = Path.Combine(directory, SpecLayout.MetaFileName);
                if (!File.Exists(metaPath))
                {
                    return;
                }

                var meta = JsonConvert.DeserializeObject<SpecMeta>(File.ReadAllText(metaPath));
                if (meta == null)
                {
                    return;
                }

                specification.SourceRef = new SpecSourceRef
                {
                    Version = meta.Version,
                    Sha256 = meta.Sha256 ?? string.Empty,
                    Range = "текст версии " + meta.Version
                };
            }
            catch (Exception)
            {
                // Ссылка на источник не критична для модели: без неё спецификация остаётся рабочей.
            }
        }

        private readonly struct SlugResolution
        {
            public SlugResolution(string slug, string error)
            {
                Slug = slug;
                Error = error;
            }

            public string Slug { get; }

            public string Error { get; }
        }

        private static SlugResolution ResolveSlug(ISpecificationStore store, string slug,
            bool allowEmpty = false, string fallbackName = null)
        {
            if (!string.IsNullOrWhiteSpace(slug))
            {
                return new SlugResolution(SpecLayout.Slugify(slug), null);
            }

            var specs = store.List();
            if (specs.Count == 1)
            {
                return new SlugResolution(specs[0].Slug, null);
            }

            if (specs.Count == 0)
            {
                if (allowEmpty)
                {
                    return new SlugResolution(SpecLayout.Slugify(fallbackName), null);
                }

                return new SlugResolution(null,
                    "в проекте нет импортированных ТЗ — сначала spec_import (каталог <проект>\\docs\\spec)");
            }

            return new SlugResolution(null,
                "ТЗ несколько — укажите slug: " + string.Join(", ", specs.Select(item => item.Slug)));
        }

        private static string ModelJson(JObject args)
        {
            var model = args["model"] ?? args["json"] ?? args["specification"] ?? args["patch"];
            if (model == null)
            {
                return null;
            }

            if (model.Type == JTokenType.String)
            {
                return model.Value<string>();
            }

            return model.ToString(Formatting.None);
        }

        private static string ArgText(JObject args, string name)
        {
            var token = args?[name];
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            return token.Type == JTokenType.String ? token.Value<string>() : token.ToString();
        }

        private static int? ArgInt(JObject args, string name)
        {
            var token = args?[name];
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            if (token.Type == JTokenType.Integer)
            {
                return token.Value<int>();
            }

            return int.TryParse(token.ToString(), out int value) ? value : (int?)null;
        }

        private static bool ArgBool(JObject args, string name)
        {
            var token = args?[name];
            if (token == null || token.Type == JTokenType.Null)
            {
                return false;
            }

            if (token.Type == JTokenType.Boolean)
            {
                return token.Value<bool>();
            }

            return bool.TryParse(token.ToString(), out bool value) && value;
        }

        private static List<string> ArgStringList(JObject args, string name)
        {
            var result = new List<string>();
            if (!(args?[name] is JArray array))
            {
                return result;
            }

            foreach (var item in array)
            {
                string value = item.Type == JTokenType.String ? item.Value<string>() : item.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result.Add(value.Trim());
                }
            }

            return result;
        }

        private static string NextId(string prefix, IEnumerable<string> existing)
        {
            int index = 1;
            var used = new HashSet<string>(
                existing.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()),
                StringComparer.OrdinalIgnoreCase);

            while (used.Contains(prefix + index))
            {
                index++;
            }

            return prefix + index;
        }
    }
}
