﻿//------------------------------------------------------------------------------
// <copyright file="SerializeThisCommand.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Threading;
using System.Threading.Tasks;
using CodeCaster.SerializeThis.Serialization;
using CodeCaster.SerializeThis.Serialization.Json;
using CodeCaster.SerializeThis.Serialization.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace CodeCaster.SerializeThis
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class SerializeThisCommand
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("c2c4513d-ca4c-4b91-be0d-b797460e7572");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly Package _package;

        /// <summary>
        /// Initializes a new instance of the <see cref="SerializeThisCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        private SerializeThisCommand(Package package)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            _package = package;

            OleMenuCommandService commandService = ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null)
            {
                var menuCommandId = new CommandID(CommandSet, CommandId);
                var menuItem = new MenuCommand(MenuItemCallback, menuCommandId);
                commandService.AddCommand(menuItem);
            }
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static SerializeThisCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private IServiceProvider ServiceProvider => _package;

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static void Initialize(Package package)
        {
            Instance = new SerializeThisCommand(package);
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
        private async void MenuItemCallback(object sender, EventArgs e)
        {
            await DoWorkAsync();
        }

        private async System.Threading.Tasks.Task DoWorkAsync()
        {

            var componentModel = ServiceProvider.GetService(typeof(SComponentModel)) as IComponentModel;
            if (componentModel == null)
            {
                return;
            }

            var vsTextManager = Package.GetGlobalService(typeof(SVsTextManager)) as IVsTextManager;
            if (vsTextManager == null)
            {
                return;
            }

            IVsTextView activeView;
            ErrorHandler.ThrowOnFailure(vsTextManager.GetActiveView(1, null, out activeView));

            var textView = componentModel.GetService<IVsEditorAdaptersFactoryService>().GetWpfTextView(activeView);

            CaretPosition caretPosition = textView.Caret.Position;
            SnapshotPoint bufferPosition = caretPosition.BufferPosition;
            var document = bufferPosition.Snapshot.GetOpenDocumentInCurrentContextWithChanges();
            if (document == null)
            {
                return;
            }

            int position = bufferPosition.Position;
            SemanticModel semanticModel = await document.GetSemanticModelAsync();

            ISymbol selectedSymbol = await GetSymbolUnderCursorAsync(document, semanticModel, position);

            var typeSymbol = selectedSymbol as ITypeSymbol;
            if (typeSymbol == null)
            {
                ShowMessageBox("Invoke this menu on a type name.");
                return;
            }

            var classInfo = new TypeSymbolParser().GetMemberInfoRecursive(typeSymbol, semanticModel);
            string memberInfoString = PrintMemberInfoRercursive(classInfo, 0);
            string json = new JsonSerializer().Serialize(classInfo);
            ShowMessageBox(memberInfoString + Environment.NewLine + Environment.NewLine + json);
        }

        private string PrintMemberInfoRercursive(ClassInfo memberInfo, int depth, Dictionary<string, string> typesSeen = null)
        {
            if (typesSeen == null)
            {
                typesSeen = new Dictionary<string, string>();
            }

            string representationForType;
            if (typesSeen.TryGetValue(memberInfo.Class.TypeName, out representationForType))
            {
                return representationForType;
            }

            string result = "";

            // First add blank, so it'll be picked up in the case of recursion (A.A or A.B.A).
            typesSeen[memberInfo.Class.TypeName] = result;

            string spaces = new string(' ', depth * 2);

            result += $"{spaces}{memberInfo.Class.TypeName} ({memberInfo.Class.Type}) {memberInfo.Name}{Environment.NewLine}";

            if (memberInfo.Class.Children != null)
            {
                foreach (var child in memberInfo.Class.Children)
                {
                    result += PrintMemberInfoRercursive(child, depth + 1, typesSeen);
                }
            }

            typesSeen[memberInfo.Class.TypeName] = result;

            return result;
        }

        private async Task<ISymbol> GetSymbolUnderCursorAsync(TextDocument document, SemanticModel semanticModel, int position)
        {
            Workspace workspace = document.Project.Solution.Workspace;
            var cancellationToken = new CancellationToken();
            ISymbol selectedSymbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, workspace, cancellationToken);
            return selectedSymbol;
        }

        private void ShowMessageBox(string message)
        {
            string title = "Serialize This";

            VsShellUtilities.ShowMessageBox(
                            ServiceProvider,
                            message,
                            title,
                            OLEMSGICON.OLEMSGICON_INFO,
                            OLEMSGBUTTON.OLEMSGBUTTON_OK,
                            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}