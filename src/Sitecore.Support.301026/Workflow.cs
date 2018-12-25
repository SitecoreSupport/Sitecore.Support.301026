
  using Sitecore;
  using Sitecore.Collections;
  using Sitecore.Configuration;
  using Sitecore.Data;
  using Sitecore.Data.Items;
  using Sitecore.Diagnostics;
  using Sitecore.Globalization;
  using Sitecore.Pipelines;
  using Sitecore.Shell.Framework.Commands;
  using Sitecore.Web.UI.Sheer;
  using Sitecore.Workflows;
  using Sitecore.Workflows.Simple;
  using System;
  using System.Collections.Generic;
  using System.Collections.Specialized;
  using System.Linq;

namespace Sitecore.Support.Shell.Framework.Commands
{
  /// <summary>
  /// Represents the Workflow command.
  /// </summary>
  [Serializable]
  public class Workflow : Command
  {
    /// <summary>Key used to identify the ID</summary>
    protected const string IDKey = "id";

    /// <summary>Key used to identify the language</summary>
    protected const string LanguageKey = "language";

    /// <summary>Key used to identify the version</summary>
    protected const string VersionKey = "version";

    /// <summary>Key used to identify the command ID</summary>
    protected const string CommandIdKey = "commandid";

    /// <summary>Key used to identify the workflow ID</summary>
    protected const string WorkflowIdKey = "workflowid";

    /// <summary>Key used to identify the UI setting</summary>
    protected const string UIKey = "ui";

    /// <summary>Key used to identify the 'check modified' setting</summary>
    protected const string CheckModifiedKey = "checkmodified";

    /// <summary>Key used to identify the 'suppress comment' setting</summary>
    protected const string SuppressCommentKey = "suppresscomment";

    /// <summary>
    /// Queries the state of the command.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>
    /// The state of the command.
    /// </returns>
    public override CommandState QueryState(CommandContext context)
    {
      if (!Settings.Workflows.Enabled)
      {
        return CommandState.Hidden;
      }
      return base.QueryState(context);
    }

    /// <summary>
    /// Executes the command in the specified context.
    /// </summary>
    /// <param name="context">The context.</param>
    public override void Execute(CommandContext context)
    {
      Assert.ArgumentNotNull(context, "context");
      string text = context.Parameters["id"];
      string text2 = context.Parameters["language"];
      string value = context.Parameters["version"];
      Item item = Client.ContentDatabase.Items[text, Language.Parse(text2), Sitecore.Data.Version.Parse(value)];
      if (item != null && CheckCommandValidity(item, context.Parameters["commandid"]))
      {
        NameValueCollection parameters = new NameValueCollection
            {
                {
                    "id",
                    text
                },
                {
                    "language",
                    text2
                },
                {
                    "version",
                    value
                },
                {
                    "commandid",
                    context.Parameters["commandid"]
                },
                {
                    "workflowid",
                    context.Parameters["workflowid"]
                },
                {
                    "ui",
                    context.Parameters["ui"]
                },
                {
                    "checkmodified",
                    "1"
                },
                {
                    "suppresscomment",
                    context.Parameters["suppresscomment"]
                }
            };
        Context.ClientPage.Start(this, "Run", parameters);
      }
    }

    /// <summary>
    /// Runs the specified args.
    /// </summary>
    /// <param name="args">The arguments.</param>
    protected void Run(ClientPipelineArgs args)
    {
      Assert.ArgumentNotNull(args, "args");
      bool flag = args.IsPostBack;
      bool flag2 = args.Parameters["checkmodified"] == "1";
      Item item = Client.ContentDatabase.Items[args.Parameters["id"], Language.Parse(args.Parameters["language"]), Sitecore.Data.Version.Parse(args.Parameters["version"])];
      if (CheckCommandValidity(item, args.Parameters["commandid"]))
      {
        if (flag2)
        {
          if (!flag)
          {
            if (Context.ClientPage.Modified)
            {
              #region Temporary removed
              //CheckModifiedParameters parameters = new CheckModifiedParameters
              //{
              //  ResumePreviousPipeline = true
              //};
              #endregion
              SheerResponse.CheckModified(true);
              args.WaitForPostBack();
              return;
            }
          }
          else if (args.Result == "cancel")
          {
            return;
          }
          args.Parameters["checkmodified"] = null;
          flag = false;
        }
        if (flag && args.Result == "cancel")
        {
          return;
        }
        Sitecore.Collections.StringDictionary commentFields = new Sitecore.Collections.StringDictionary();
        bool flag3 = StringUtil.GetString(args.Parameters["ui"]) != "1";
        bool flag4 = StringUtil.GetString(args.Parameters["suppresscomment"]) == "1";
        string text = args.Parameters["commandid"];
        string workflowId = args.Parameters["workflowid"];
        ItemUri itemUri = new ItemUri(args.Parameters["id"], Language.Parse(args.Parameters["language"]), Sitecore.Data.Version.Parse(args.Parameters["version"]), Client.ContentDatabase);
        if ((!flag & flag3) && !flag4)
        {
          ID result = ID.Null;
          ID.TryParse(text, out result);
          WorkflowUIHelper.DisplayCommentDialog(new List<ItemUri>
                {
                    itemUri
                }, result);
          args.WaitForPostBack();
        }
        else
        {
          if (flag)
          {
            if (args.Result == "null" || args.Result == "undefined")
            {
              return;
            }
            string result2 = args.Result;
            commentFields = WorkflowUIHelper.ExtractFieldsFromFieldEditor(result2);
          }
          Processor completionCallback = new Processor("Workflow completed callback", this, "WorkflowCompleteCallback");
          WorkflowUIHelper.ExecuteCommand(itemUri, workflowId, text, commentFields, completionCallback);
        }
      }
    }

    /// <summary>
    /// Processor delegate to be executed when workflow completes successfully.
    /// </summary>
    /// <param name="args">The arguments for the workflow invocation.</param>
    [UsedImplicitly]
    protected void WorkflowCompleteCallback(WorkflowPipelineArgs args)
    {
      Context.ClientPage.SendMessage(this, "item:refresh");
    }

    /// <summary>
    /// Checks if this command can be executed against current workflow state. This is mainly about concurrent workflow transitions.
    /// </summary>
    /// <param name="item">the item</param>
    /// <param name="commandId">workflow command</param>
    /// <returns></returns>
    private bool CheckCommandValidity(Item item, string commandId)
    {
      Assert.ArgumentNotNullOrEmpty(commandId, "commandId");
      Assert.ArgumentNotNull(item, "item");
      IWorkflow workflow = item.State.GetWorkflow();
      WorkflowState workflowState = item.State.GetWorkflowState();
      Assert.IsNotNull(workflow, "workflow");
      Assert.IsNotNull(workflowState, "state");
      if (!workflow.GetCommands(workflowState.StateID).Any((WorkflowCommand a) => a.CommandID == commandId))
      {
        SheerResponse.Alert("The item has been moved to a different workflow state. Sitecore will therefore reload the item.", Array.Empty<string>());
        Context.ClientPage.SendMessage(this, "item:refresh");
        return false;
      }
      return true;
    }
  }
}