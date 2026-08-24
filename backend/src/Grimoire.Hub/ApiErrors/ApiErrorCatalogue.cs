namespace Grimoire.Hub.ApiErrors;

/// <summary>
/// Every failure the Hub's HTTP API can return, in one place (ADR-026; 024 FR-016, SC-006).
///
/// <para>
/// A catalogue rather than message literals at the twenty-odd call sites, because SC-006 promises
/// that <i>every</i> code resolves to authored prose. With literals scattered across five endpoint
/// namespaces that promise can only be established by a human reading every endpoint; here it is a
/// test over a real collection. Under Constitution Principle IV an unverifiable guarantee is not a
/// guarantee.
/// </para>
///
/// <para>
/// Codes that already existed on the wire as <c>reason</c> values are carried over verbatim
/// (024 FR-003): tests, logs and operational tooling key on them, and two of them —
/// <c>conversation_record_unreadable</c> (ADR-014) and <c>lint_run_active</c> (ADR-020) — are
/// pinned by an accepted ADR.
/// </para>
/// </summary>
public static class ApiErrorCatalogue
{
    // ---------------------------------------------------------------------
    // Generic fallbacks. Not decoration: they are what makes "every response carries readable
    // prose" hole-free when a code is unknown to the catalogue (FR-016).
    // ---------------------------------------------------------------------

    public const string InternalError = "internal_error";
    public const string RequestDeclined = "request_declined";

    // --- Cross-cutting -----------------------------------------------------

    public const string EndpointNotFound = "endpoint_not_found";

    // --- Ingest submission -------------------------------------------------

    public const string IngestSubmissionBodyInvalid = "ingest_submission_body_invalid";
    public const string IngestSubmissionKindInvalid = "ingest_submission_kind_invalid";
    public const string IngestSubmissionFileMissing = "ingest_submission_file_missing";
    public const string IngestSubmissionConvertStepsInvalid = "ingest_submission_convert_steps_invalid";
    public const string IngestSubmissionInvalid = "ingest_submission_invalid";
    public const string IngestSubmissionUnsupportedMediaType = "ingest_submission_unsupported_media_type";
    public const string IngestSubmissionUnprocessable = "ingest_submission_unprocessable";
    public const string UserPromptTooLong = "user_prompt_too_long";
    public const string UnknownConvertStep = "unknown_convert_step";
    public const string ConvertStepNotApplicable = "convert_step_not_applicable";
    public const string ConvertStepRequired = "convert_step_required";
    public const string IngestTaskNotFound = "ingest_task_not_found";
    public const string IngestSourceContentNotFound = "ingest_source_content_not_found";
    public const string IngestTaskRecordUnavailable = "ingest_task_record_unavailable";
    public const string IngestTaskNotQueued = "ingest_task_not_queued";
    public const string DefaultUserPromptMissing = "default_user_prompt_missing";
    public const string DefaultUserPromptEmpty = "default_user_prompt_empty";

    // --- Ingest restart (ADR-025) ------------------------------------------

    public const string RestartTaskNotFailed = "restart_task_not_failed";
    public const string RestartSourceMissing = "restart_source_missing";
    public const string RestartAlreadyInProgress = "restart_already_in_progress";

    // --- Ingest cancel (issue #184) -----------------------------------------

    public const string IngestTaskNotRunning = "ingest_task_not_running";

    // --- Query -------------------------------------------------------------

    public const string QuerySubmissionBodyRequired = "query_submission_body_required";
    public const string QueryConversationIdInvalid = "query_conversation_id_invalid";
    public const string QuerySubmissionInvalid = "query_submission_invalid";
    public const string QueryConcurrencyLimitReached = "query_concurrency_limit_reached";
    public const string ConversationAlreadyActive = "conversation_already_active";
    public const string ConversationRecordUnreadable = "conversation_record_unreadable";
    public const string QueryTurnNotFound = "query_turn_not_found";

    // --- Lint --------------------------------------------------------------

    public const string LintRunActive = "lint_run_active";
    public const string UnresolvedRemediationTasks = "unresolved_remediation_tasks";
    public const string LintRunNotFound = "lint_run_not_found";
    public const string LintFindingsReportUnavailable = "lint_findings_report_unavailable";

    // --- Remediation (ADR-018) ---------------------------------------------

    public const string RemediationTaskNotFound = "remediation_task_not_found";
    public const string TaskNotProposed = "task_not_proposed";
    public const string TaskNotAuthorized = "task_not_authorized";
    public const string ExecutionAlreadyStarted = "execution_already_started";
    public const string MessageTurnActive = "message_turn_active";
    public const string RemediationMessageInvalid = "remediation_message_invalid";
    public const string RemediationAttachmentInvalid = "remediation_attachment_invalid";

    private static readonly ApiErrorDefinition[] _definitions =
    [
        new(InternalError, 500,
            "Something went wrong",
            "The request could not be completed because of an internal error. This is not caused by your input — try again in a moment."),
        new(RequestDeclined, 400,
            "Request declined",
            "The request could not be completed as sent. Check what you submitted and try again."),

        // --- Cross-cutting ---
        new(EndpointNotFound, 404,
            "No such endpoint",
            "This address is not part of the Hub's API. Check the path, or the version of the client that produced it."),

        // --- Ingest submission ---
        new(IngestSubmissionBodyInvalid, 400,
            "Submission could not be read",
            "The submission could not be read as valid JSON. Send a well-formed request body."),
        new(IngestSubmissionKindInvalid, 400,
            "Unsupported submission type",
            "This submission type is not supported. Submit a URL, or a Markdown, PDF, or Office document."),
        new(IngestSubmissionFileMissing, 400,
            "No file attached",
            "A file submission needs a file. Choose a document and submit again."),
        new(IngestSubmissionConvertStepsInvalid, 400,
            "Conversion settings could not be read",
            "The conversion settings were not in the expected form. Each step must be switched on or off."),
        new(IngestSubmissionInvalid, 400,
            "Submission could not be accepted",
            "The submission could not be accepted as sent. Check the source and try again."),
        new(IngestSubmissionUnsupportedMediaType, 415,
            "Unsupported file format",
            "This file format cannot be ingested. Submit Markdown, PDF, or an Office document."),
        new(IngestSubmissionUnprocessable, 422,
            "Source could not be processed",
            "The source was recognized but could not be processed. Check that it is reachable and not empty."),
        // The four validator-owned failures. Their identifiers used to live glued to the front of
        // the validator's own message text; carrying them here is what lets the endpoint answer
        // with the code and the prose in separate members.
        new(UserPromptTooLong, 400,
            "Steering prompt is too long",
            "The steering prompt is longer than this wiki allows. Shorten it and submit again."),
        new(UnknownConvertStep, 400,
            "Unknown conversion step",
            "One of the conversion steps in this submission is not one this wiki knows about."),
        new(ConvertStepNotApplicable, 400,
            "Conversion step does not apply",
            "One of the conversion steps in this submission does not apply to this kind of source."),
        new(ConvertStepRequired, 422,
            "Conversion step cannot be switched off",
            "A conversion step this source type requires was switched off. Binary formats must be converted to Markdown before an agent can read them."),

        new(IngestTaskNotFound, 404,
            "Task not found",
            "This task no longer exists, or the link that led here is out of date."),
        new(IngestSourceContentNotFound, 404,
            "Original source not available",
            "The original source for this task was not kept, so it cannot be shown."),
        new(IngestTaskRecordUnavailable, 404,
            "Task record not available",
            "This task has not written its record yet. It becomes available once the task has run."),
        new(IngestTaskNotQueued, 409,
            "Task is not waiting in the queue",
            "This task has already moved on from the queue, so it cannot be changed there."),
        new(DefaultUserPromptMissing, 500,
            "Default prompt is not configured",
            "The default prompt document is missing on the server. The wiki's configuration needs attention before ingest can use it."),
        new(DefaultUserPromptEmpty, 500,
            "Default prompt is empty",
            "The default prompt document on the server has no content. The wiki's configuration needs attention before ingest can use it."),

        // --- Ingest restart (ADR-025) ---
        new(RestartTaskNotFailed, 409,
            "Task is not failed",
            "Only a failed task can be restarted. This one is still going, or already finished."),
        // No retry can fix this one and no user action on this task can either, so the detail
        // names the actual way forward rather than inviting a pointless retry (ADR-025).
        new(RestartSourceMissing, 409,
            "Original source is gone",
            "The stored source for this task is no longer available, so it cannot be restarted. Submit the source again as a new task."),
        new(RestartAlreadyInProgress, 409,
            "Restart already under way",
            "This task is already restarting. Wait for it to pick up again."),

        // --- Ingest cancel (issue #184) ---
        new(IngestTaskNotRunning, 409,
            "Task is not running",
            "Only the task currently occupying the agent slot can be cancelled. This one is queued, already finished, or does not exist."),

        // --- Query ---
        new(QuerySubmissionBodyRequired, 400,
            "Question is missing",
            "A question is required. Type what you want to ask and submit again."),
        new(QueryConversationIdInvalid, 400,
            "Conversation reference is not valid",
            "The conversation this question belongs to could not be identified. Start a new conversation and ask again."),
        new(QuerySubmissionInvalid, 400,
            "Question could not be accepted",
            "The question could not be accepted as sent. Check it and try again."),
        new(QueryConcurrencyLimitReached, 503,
            "Too many questions at once",
            "The wiki is answering as many questions as it can handle right now. Wait a moment and ask again."),
        new(ConversationAlreadyActive, 409,
            "Conversation is busy",
            "This conversation is still working on the previous question. Wait for the answer, then ask again."),
        // ADR-014 fail-closed: a 500, but retrying re-fails deterministically until the operator
        // starts a new conversation — so the detail names that, rather than "try again".
        new(ConversationRecordUnreadable, 500,
            "Conversation history cannot be read",
            "This conversation's history is damaged and cannot be read, so continuing it would risk a wrong answer. Start a new conversation to keep going."),
        new(QueryTurnNotFound, 404,
            "Question not found",
            "This question no longer exists, or the link that led here is out of date."),

        // --- Lint ---
        new(LintRunActive, 409,
            "A lint run is already going",
            "A lint run is already active. Wait for it to finish before starting another."),
        new(UnresolvedRemediationTasks, 409,
            "Earlier remediation tasks are still open",
            "Remediation tasks from the previous lint run are still unresolved. Authorize or dismiss them, then start a new run."),
        new(LintRunNotFound, 404,
            "Lint run not found",
            "This lint run no longer exists, or the link that led here is out of date."),
        new(LintFindingsReportUnavailable, 404,
            "Findings report not available",
            "This run has not written a findings report. It becomes available once the run has completed."),

        // --- Remediation (ADR-018: each conflict keeps its own message so the board shows the
        //     actual outcome rather than a shared "declined") ---
        new(RemediationTaskNotFound, 404,
            "Remediation task not found",
            "This remediation task no longer exists, or the link that led here is out of date."),
        new(TaskNotProposed, 409,
            "Task has already been decided",
            "Only a proposed task can be authorized or dismissed. This one has already moved on."),
        new(TaskNotAuthorized, 409,
            "Task is not authorized",
            "Only an authorized task can have its authorization withdrawn. This one is not authorized."),
        new(ExecutionAlreadyStarted, 409,
            "Task is already running",
            "The agent has already begun this task; it will run to an outcome and can no longer be withdrawn."),
        new(MessageTurnActive, 409,
            "Task is still replying",
            "This task is already working on your previous message. Wait for the reply before sending another."),
        new(RemediationMessageInvalid, 400,
            "Message could not be accepted",
            "The message could not be accepted as sent. Check it and try again."),
        new(RemediationAttachmentInvalid, 400,
            "Attachment could not be accepted",
            "The attachment could not be accepted as sent. Check it and try again."),
    ];

    private static readonly Dictionary<string, ApiErrorDefinition> _byCode =
        _definitions.ToDictionary(d => d.Code, StringComparer.Ordinal);

    /// <summary>Every definition, for the completeness assertions in <c>HubApiErrorEnvelopeTests</c>.</summary>
    public static IReadOnlyCollection<ApiErrorDefinition> All => _definitions;

    /// <summary>
    /// The definition for <paramref name="code"/>, or a generic fallback when the code is unknown.
    ///
    /// <para>
    /// Falling back rather than throwing or echoing the code is FR-016: a code shipped without a
    /// catalogue entry is a defect, but the user should still get a sentence rather than an
    /// identifier or a 500 from the error path itself. The fallback is chosen by shape — an unknown
    /// code paired with a 5xx status is a fault, anything else is a declined request.
    /// </para>
    /// </summary>
    public static ApiErrorDefinition Resolve(string code, int fallbackStatus = 400)
        => _byCode.TryGetValue(code, out var definition)
            ? definition
            : _byCode[fallbackStatus >= 500 ? InternalError : RequestDeclined];

    /// <summary>True when <paramref name="code"/> has an authored definition.</summary>
    public static bool Contains(string code) => _byCode.ContainsKey(code);
}
