﻿using FlatRedBall.Glue.Events;
using FlatRedBall.Glue.FormHelpers;
using FlatRedBall.Glue.Plugins.ExportedImplementations;
using FlatRedBall.Glue.SaveClasses;
using OfficialPlugins.TreeViewPlugin.ViewModels;
using OfficialPlugins.TreeViewPlugin.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace OfficialPlugins.TreeViewPlugin.Logic
{
    internal static class SelectionLogic
    {
        #region Fields/Properties

        static MainTreeViewViewModel mainViewModel;
        static MainTreeViewControl mainView;

        static List<NodeViewModel> currentNodes = new List<NodeViewModel>();

        public static bool IsUpdatingThisSelectionOnGlueEvent = true;
        public static bool IsPushingSelectionOutToGlue = true;

        public static NodeViewModel CurrentNode
        {
            get => currentNodes.FirstOrDefault();
        }

        public static NamedObjectSave CurrentNamedObjectSave
        {
            set => SelectByTag(value, false);
        }

        public static ReferencedFileSave CurrentReferencedFileSave
        {
            set => SelectByTag(value, false);
        }

        public static CustomVariable CurrentCustomVariable
        {
            set => SelectByTag(value, false);
        }

        public static EventResponseSave CurrentEventResponseSave
        {
            set => SelectByTag(value, false);
        }

        public static StateSave CurrentStateSave
        {
            set => SelectByTag(value, false);
        }

        public static StateSaveCategory CurrentStateSaveCategory
        {
            set => SelectByTag(value, false);
        }

        public static EntitySave CurrentEntitySave
        {
            set => SelectByTag(value, false);
        }

        public static ScreenSave CurrentScreenSave
        {
            set => SelectByTag(value, false);
        }

        #endregion

        public static void HandleDeselection(NodeViewModel nodeViewModel)
        {
            if (currentNodes.Contains(nodeViewModel))
            {
                currentNodes.Remove(nodeViewModel);
            }

            RefreshGlueState(true);
        }

        public static void HandleSelected(NodeViewModel nodeViewModel, bool focus = true)
        {
            IsUpdatingThisSelectionOnGlueEvent = false;

            var newTag = nodeViewModel.Tag;

            bool didSelectionChange;
            if (currentNodes?.Contains(nodeViewModel) == true)
            {
                didSelectionChange = false;
            }
            else if (currentNodes.Count == 0 && newTag == null)
            {
                didSelectionChange = false;
            }
            else if (currentNodes.Count > 0 && nodeViewModel == null)
            {
                didSelectionChange = true;
            }
            else if (currentNodes.Count == 0 && nodeViewModel != null)
            {
                didSelectionChange = true;
            }
            else
            {
                didSelectionChange = currentNodes.Any(item => item.Tag == nodeViewModel.Tag) == false;
            }

            if (nodeViewModel != null)
            {
                currentNodes.Add(nodeViewModel);
            }

            if (nodeViewModel != null && nodeViewModel.IsSelected && focus)
            {
                nodeViewModel.Focus(mainView);
            }

            RefreshGlueState(didSelectionChange);

            IsUpdatingThisSelectionOnGlueEvent = true;

        }

        private static void RefreshGlueState(bool didSelectionChange)
        {
            if (IsPushingSelectionOutToGlue
                // The node can change if the user deletes a tree node and then a new one
                // automatically gets re-selected. In this case, we do still want to push the selection out.
                || didSelectionChange)
            {
                //var tag = nodeViewModel.Tag;

                //if (tag is NamedObjectSave nos)
                //{
                //    GlueState.Self.CurrentNamedObjectSave = nos;
                //}
                //else if (tag is ReferencedFileSave rfs)
                //{
                //    GlueState.Self.CurrentReferencedFileSave = rfs;
                //}
                //else if (tag is CustomVariable variable)
                //{
                //    GlueState.Self.CurrentCustomVariable = variable;
                //}
                //else if (tag is EventResponseSave eventResponse)
                //{
                //    GlueState.Self.CurrentEventResponseSave = eventResponse;
                //}
                //else if (tag is StateSave state)
                //{
                //    GlueState.Self.CurrentStateSave = state;
                //}
                //else if (tag is StateSaveCategory stateCategory)
                //{
                //    GlueState.Self.CurrentStateSaveCategory = stateCategory;
                //}
                //else if (tag is EntitySave entitySave)
                //{
                //    GlueState.Self.CurrentEntitySave = entitySave;
                //}
                //else if (tag is ScreenSave screenSave)
                //{
                //    GlueState.Self.CurrentScreenSave = screenSave;
                //}
                //else if (tag == null)
                //{
                //    GlueState.Self.CurrentTreeNode = nodeViewModel;
                //}
                GlueState.Self.CurrentTreeNodes = currentNodes;
            }

            // We used to refresh here on a normal click. This is unnecessary
            // since most of the time the right-click menu isn't accessed. Moved this to preview
            // right click in TMainTreeviewControl.xaml.cs
            //RefreshRightClickMenu();
            // Update April 16, 2023
            // We should assign this because if the user directly right-clicks on a new node,
            // we want this to get called

            mainView.RefreshRightClickMenu();
        }

        internal static async void SelectByPath(string path, bool addToSelection)
        {
            var treeNode = mainViewModel.GetTreeNodeByRelativePath(path);
            await SelectByTreeNode(treeNode, addToSelection);
        }

        public static async void SelectByTag(object value, bool addToSelection)
        {
            NodeViewModel treeNode = value == null ? null : mainViewModel.GetTreeNodeByTag(value);

            await SelectByTreeNode(treeNode, addToSelection);

        }

        public static bool SuppressFocus = false;

        public static async Task SelectByTreeNode(NodeViewModel treeNode, bool addToSelection, bool selectAndScroll = true)
        {
            // record the value here since we delay on this method
            var suppressFocusCopy = SuppressFocus;
            if (treeNode == null)
            {
                if (currentNodes.Count > 0 && !addToSelection)
                {
                    SelectionLogic.IsUpdatingThisSelectionOnGlueEvent = false;

                    mainViewModel.DeselectResursively();
                    //currentNode.IsSelected = false;
                    currentNodes.Clear();

                    SelectionLogic.IsUpdatingThisSelectionOnGlueEvent = true;
                }
            }
            else
            {
                if (treeNode != null && (treeNode.IsSelected == false || currentNodes.Contains(treeNode) == false))
                {
                    if(CurrentNode?.IsSelected == false && !addToSelection)
                    {
                        mainViewModel.DeselectResursively();
                        // Selecting a tree node deselects the current node, but that can take some time and cause
                        // some inconsistent behavior. To solve this, we will forcefully deselect the current node 
                        // so the consequence of selecting this node is immediate:
                        foreach(var node in currentNodes)
                        {
                            node.IsSelected = false;
                        }
                        // do we null out currentNode
                    }
                    if(suppressFocusCopy)
                    {
                        treeNode.SelectNoFocus();
                        if(addToSelection)
                        {
                            if(currentNodes.Contains(treeNode) == false)
                            {
                                currentNodes.Add(treeNode);
                            }
                        }
                        else
                        {
                            currentNodes.Clear();
                            currentNodes.Add(treeNode);
                        }
                    }
                    else
                    {
                        treeNode.IsSelected = true;
                    }

                    if(selectAndScroll)
                    {
                        treeNode.ExpandParentsRecursively();
                    }
                }

                if (selectAndScroll)
                {
                    // If we don't do this, sometimes it doesn't scroll into view...
                    await System.Threading.Tasks.Task.Delay(120);

                    mainView.MainTreeView.UpdateLayout();

                    mainView.MainTreeView.ScrollIntoView(treeNode);

                    // Do this after the delay
                    if(treeNode?.IsSelected == true && !suppressFocusCopy)
                    {
                        treeNode.Focus(mainView);
                    }
                }
            }
        }

        public static void Initialize(MainTreeViewViewModel mainViewModel, MainTreeViewControl mainView)
        {
            SelectionLogic.mainViewModel = mainViewModel;
            SelectionLogic.mainView = mainView;
        }
    }
}
