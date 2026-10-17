using System;
using System.Collections.Generic;
using LiveLink;
using UnityEngine;

namespace LiveLink.Editor.Identity
{
    public enum IdentityIssueType
    {
        MissingObjectId,
        MissingAuthoringKey,
        OwnershipMismatch,
        DuplicateObjectId,
        AmbiguousDuplicate,
        UnsavedScene
    }

    public enum IdentityIssueSeverity
    {
        Warning,
        Error
    }

    public enum IdentityRepairAction
    {
        None,
        AdoptIdentity,
        ReassignIdentity,
        ManualResolution
    }

    public sealed class IdentityValidationIssue
    {
        public IdentityIssueType Type { get; }
        public IdentityIssueSeverity Severity { get; }
        public IdentityRepairAction RecommendedAction { get; }
        public LiveLinkIdentity Identity { get; }
        public string ObjectId { get; }
        public string StoredAuthoringKey { get; }
        public string ActualAuthoringKey { get; }
        public string Message { get; }

        public IdentityValidationIssue(
            IdentityIssueType type,
            IdentityIssueSeverity severity,
            IdentityRepairAction action,
            LiveLinkIdentity identity,
            string objectId,
            string storedAuthoringKey,
            string actualAuthoringKey,
            string message)
        {
            Type = type;
            Severity = severity;
            RecommendedAction = action;
            Identity = identity;
            ObjectId = objectId;
            StoredAuthoringKey = storedAuthoringKey;
            ActualAuthoringKey = actualAuthoringKey;
            Message = message;
        }
    }

    public sealed class IdentityValidationResult
    {
        public List<IdentityValidationIssue> Issues { get; } = new List<IdentityValidationIssue>();
        public bool IsValid => Issues.Count == 0;

        public int ErrorCount
        {
            get
            {
                int count = 0;
                foreach (var issue in Issues)
                    if (issue.Severity == IdentityIssueSeverity.Error)
                        count++;
                return count;
            }
        }

        public void Log(string prefix = "[LiveLink]")
        {
            if (IsValid)
            {
                return;
            }

            foreach (var issue in Issues)
            {
                string message = prefix + " " + issue.Message;
                UnityEngine.Object context = issue.Identity;

                if (issue.Severity == IdentityIssueSeverity.Error)
                {
                    if (context == null)
                        Debug.LogError(message);
                    else
                        Debug.LogError(message, context);
                }
                else
                {
                    if (context == null)
                        Debug.LogWarning(message);
                    else
                        Debug.LogWarning(message, context);
                }
            }

            Debug.Log(prefix + " Identity validation found " + Issues.Count + " issue(s).");
        }
    }
}
