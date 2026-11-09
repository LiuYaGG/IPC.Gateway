using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using IPC.Runtime.Configuration;
using IPC.Runtime.Values;

namespace IPC.EdgeGateway
{
    public sealed partial class EdgeRuleEngineService
    {
        private const int RelatedSnapshotMaxAgeSeconds = 300;
        private readonly Dictionary<string, List<EdgeRuleConfig>> _rulesByTagId =
            new Dictionary<string, List<EdgeRuleConfig>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<EdgeRuleConfig>> _rulesByToken =
            new Dictionary<string, List<EdgeRuleConfig>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<EdgeRuleConfig> _rulesWithoutDependencies = new List<EdgeRuleConfig>();
        private readonly List<EdgeRuleConfig> _timerRules = new List<EdgeRuleConfig>();
        private readonly Dictionary<string, TagValueSnapshot> _snapshotsByChannelPath =
            new Dictionary<string, TagValueSnapshot>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _ambiguousSnapshotPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _ambiguousSnapshotPoints =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void BuildRuleDependencyIndex()
        {
            _rulesByTagId.Clear();
            _rulesByToken.Clear();
            _rulesWithoutDependencies.Clear();
            _timerRules.Clear();

            List<EdgeRuleConfig> rules = GetRules();
            for (int i = 0; i < rules.Count; i++)
            {
                EdgeRuleConfig rule = rules[i];
                if (rule == null || !rule.Enabled)
                    continue;

                if (NeedsRuleTimer(rule))
                    _timerRules.Add(rule);

                bool indexed = false;
                indexed |= AddRuleTagDependency(rule.SourceTagId, rule);
                indexed |= AddRuleTagDependency(rule.RelatedTagId, rule);
                indexed |= AddRuleTagDependency(rule.ContextTagId, rule);

                if (rule.Conditions != null)
                {
                    for (int conditionIndex = 0; conditionIndex < rule.Conditions.Count; conditionIndex++)
                    {
                        EdgeRuleConditionConfig condition = rule.Conditions[conditionIndex];
                        if (condition != null)
                            indexed |= AddRuleTagDependency(condition.SourceTagId, rule);
                    }
                }

                if (rule.ConditionType == EdgeRuleConditionType.Expression)
                {
                    foreach (Match match in Regex.Matches(rule.Expression ?? string.Empty, "\\{([^{}]+)\\}"))
                    {
                        string token = match.Groups[1].Value.Trim();
                        if (!string.Equals(token, "value", StringComparison.OrdinalIgnoreCase))
                            indexed |= AddRuleTokenDependency(token, rule);
                    }
                }

                if (rule.ConditionType == EdgeRuleConditionType.ModelInference)
                {
                    List<string> modelInputs = SplitModelInputTags(rule.ModelInputTags);
                    for (int inputIndex = 0; inputIndex < modelInputs.Count; inputIndex++)
                        indexed |= AddRuleTokenDependency(modelInputs[inputIndex], rule);
                }

                if (!indexed)
                    _rulesWithoutDependencies.Add(rule);
            }
        }

        private List<EdgeRuleConfig> GetCandidateRules(TagValueSnapshot snapshot)
        {
            List<EdgeRuleConfig> result = new List<EdgeRuleConfig>();
            HashSet<string> added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (snapshot == null)
                return result;

            AddIndexedRules(_rulesByTagId, snapshot.TagId, result, added);
            AddIndexedRules(_rulesByToken, snapshot.TagId, result, added);
            AddIndexedRules(_rulesByToken, GetPointCode(snapshot), result, added);
            AddIndexedRules(_rulesByToken, BuildChannelSnapshotPath(snapshot), result, added);
            AddIndexedRules(_rulesByToken, BuildLegacySnapshotPath(snapshot), result, added);

            for (int i = 0; i < _rulesWithoutDependencies.Count; i++)
                AddCandidateRule(_rulesWithoutDependencies[i], result, added);

            return result;
        }

        private bool AddRuleTagDependency(string tagId, EdgeRuleConfig rule)
        {
            return AddRuleDependency(_rulesByTagId, tagId, rule);
        }

        private bool AddRuleTokenDependency(string token, EdgeRuleConfig rule)
        {
            return AddRuleDependency(_rulesByToken, token, rule);
        }

        private static bool AddRuleDependency(
            Dictionary<string, List<EdgeRuleConfig>> index,
            string key,
            EdgeRuleConfig rule)
        {
            string normalized = NullToEmpty(key).Trim();
            if (string.IsNullOrWhiteSpace(normalized) || rule == null)
                return false;

            if (!index.TryGetValue(normalized, out List<EdgeRuleConfig>? rules))
            {
                rules = new List<EdgeRuleConfig>();
                index[normalized] = rules;
            }

            string ruleId = GetRuleId(rule);
            if (!rules.Any(item => string.Equals(GetRuleId(item), ruleId, StringComparison.OrdinalIgnoreCase)))
                rules.Add(rule);
            return true;
        }

        private static void AddIndexedRules(
            Dictionary<string, List<EdgeRuleConfig>> index,
            string key,
            List<EdgeRuleConfig> target,
            HashSet<string> added)
        {
            string normalized = NullToEmpty(key).Trim();
            if (string.IsNullOrWhiteSpace(normalized) || !index.TryGetValue(normalized, out List<EdgeRuleConfig>? rules))
                return;

            for (int i = 0; i < rules.Count; i++)
                AddCandidateRule(rules[i], target, added);
        }

        private static void AddCandidateRule(
            EdgeRuleConfig rule,
            List<EdgeRuleConfig> target,
            HashSet<string> added)
        {
            if (rule == null)
                return;

            string id = GetRuleId(rule);
            if (added.Add(id))
                target.Add(rule);
        }

        private static string BuildChannelSnapshotPath(TagValueSnapshot snapshot)
        {
            if (snapshot == null)
                return string.Empty;

            string group = NullToEmpty(snapshot.GroupName).Trim();
            return string.Join(
                ".",
                new[]
                {
                    NullToEmpty(snapshot.ChannelName).Trim(),
                    NullToEmpty(snapshot.DeviceName).Trim(),
                    group,
                    NullToEmpty(snapshot.TagName).Trim()
                }.Where(item => !string.IsNullOrWhiteSpace(item)));
        }

        private static string BuildLegacySnapshotPath(TagValueSnapshot snapshot)
        {
            if (snapshot == null)
                return string.Empty;

            string group = NullToEmpty(snapshot.GroupName).Trim();
            return string.Join(
                ".",
                new[]
                {
                    NullToEmpty(snapshot.DeviceName).Trim(),
                    group,
                    NullToEmpty(snapshot.TagName).Trim()
                }.Where(item => !string.IsNullOrWhiteSpace(item)));
        }

        private static bool IsSnapshotFresh(TagValueSnapshot snapshot, TagValueSnapshot referenceSnapshot)
        {
            if (snapshot == null)
                return false;
            if (snapshot.Timestamp == DateTime.MinValue || referenceSnapshot == null || referenceSnapshot.Timestamp == DateTime.MinValue)
                return true;

            return Math.Abs((referenceSnapshot.Timestamp - snapshot.Timestamp).TotalSeconds) <= RelatedSnapshotMaxAgeSeconds;
        }
    }
}
