﻿using EditorObjects.Parsing;
using FlatRedBall.Glue.CodeGeneration;
using FlatRedBall.Glue.Controls;
using FlatRedBall.Glue.Elements;
using FlatRedBall.Glue.Errors;
using FlatRedBall.Glue.FormHelpers;
using FlatRedBall.Glue.IO;
using FlatRedBall.Glue.Parsing;
using FlatRedBall.Glue.Plugins;
using FlatRedBall.Glue.Plugins.ExportedImplementations;
using FlatRedBall.Glue.Plugins.ExportedImplementations.CommandInterfaces;
using FlatRedBall.Glue.SaveClasses;
using FlatRedBall.Glue.StandardTypes;
using FlatRedBall.Glue.ViewModels;
using FlatRedBall.IO;
using FlatRedBall.Utilities;
using Glue;
using GlueFormsCore.Managers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using GeneralResponse = ToolsUtilities.GeneralResponse;

namespace FlatRedBall.Glue.Managers
{
    public class DragDropManager : Singleton<DragDropManager>
    {
        #region Named Object

        private void MoveNamedObject(ITreeNode treeNodeMoving, ITreeNode targetNode)
        {
            if (targetNode != null)
            {
                NamedObjectSave targetNos = targetNode.Tag as NamedObjectSave;
                NamedObjectSave movingNos = treeNodeMoving.Tag as NamedObjectSave;

                bool succeeded = false;
                if (targetNode == null)
                {
                    // Didn't move on to anything
                }
                else if (targetNode.IsRootNamedObjectNode())
                {
                    succeeded = MoveObjectOnObjectsRoot(treeNodeMoving, targetNode, movingNos, succeeded);
                }
                else if (targetNode.IsRootCustomVariablesNode())
                {
                    MoveObjectOnRootCustomVariablesNode(treeNodeMoving, targetNode);
                }
                else if (targetNode.Tag is GlueElement glueElement)
                {
                    succeeded = DragDropNosIntoElement(movingNos, glueElement);
                }
                else if (targetNode.IsRootEventsNode())
                {
                    succeeded = DragDropNosOnRootEventsNode(treeNodeMoving, targetNode);
                }
                else if (targetNos != null && targetNos.SourceType == SourceType.FlatRedBallType)
                {
                    succeeded = DragDropNosOnNos(treeNodeMoving, targetNode, targetNos, movingNos, succeeded);

                }
                else
                {
                    MessageBox.Show("Invalid movement");
                }


                if (succeeded)
                {
                    var element = targetNode.GetContainingElementTreeNode()?.Tag as GlueElement;
                    if (element != null)
                    {
                        GlueCommands.Self.GenerateCodeCommands.GenerateElementCode(element);
                    }
                    else
                    {
                        GlobalContentCodeGenerator.UpdateLoadGlobalContentCode();
                    }
                    GlueCommands.Self.ProjectCommands.SaveProjects();
                    GluxCommands.Self.SaveGlux();
                }
            }
        }

        private bool DragDropNosOnNos(ITreeNode treeNodeMoving, ITreeNode targetNode, NamedObjectSave targetNos, NamedObjectSave movingNos, bool succeeded)
        {
            var targetAti = targetNos.GetAssetTypeInfo();
            string targetClassType = targetAti?.FriendlyName;

            bool canBeMovedInList = false;
            bool canBeCollidable = false;


            var element = ObjectFinder.Self.GetElementContaining(targetNos);

            #region Failure cases

            if (string.IsNullOrEmpty(targetClassType))
            {
                MessageBox.Show("The target Object does not have a defined type.  This operation is not valid");
            }

            #endregion

            #region On Layer

            else if (targetAti == AvailableAssetTypes.CommonAtis.Layer)
            {
                // Only allow this if the NOS's are on the same object
                if (ObjectFinder.Self.GetElementContaining(movingNos) ==
                    ObjectFinder.Self.GetElementContaining(targetNos))
                {
                    succeeded = true;
                    movingNos.LayerOn = targetNos.InstanceName;
                    MainGlueWindow.Self.PropertyGrid.Refresh();

                    GlueCommands.Self.RefreshCommands.RefreshTreeNodeFor(element);
                    GlueState.Self.CurrentNamedObjectSave = movingNos;
                }
            }

            #endregion

            #region On ShapeCollection

            else if (targetAti == AvailableAssetTypes.CommonAtis.ShapeCollection)
            {
                var response = HandleDropOnShapeCollection(treeNodeMoving, targetNode, targetNos, movingNos);

                if (!response.Succeeded && IsCollidableOrCollidableList(movingNos))
                {
                    response = HandleCreateCollisionRelationship(movingNos, targetNos);
                }

                if (!response.Succeeded)
                {
                    MessageBox.Show($"Could not drop {movingNos} on {targetNos}");

                }

                succeeded = response.Succeeded;
            }

            #endregion

            else
            {
                if (IsCollidableOrCollidableList(movingNos) && IsCollidableOrCollidableList(targetNos))
                {
                    canBeCollidable = true;
                }
                //else if(IsCollidable(movingNos) && IsCollidableList(targetNos) && movingNos.CanBeInList(targetNos) == false)
                //{
                //    var response = HandleCreateCollisionRelationship(movingNos, targetNos);

                //    if (!response.Succeeded)
                //    {
                //        MessageBox.Show(response.Message);
                //    }

                //    succeeded = response.Succeeded;
                //}
                if (targetAti == AvailableAssetTypes.CommonAtis.PositionedObjectList)
                {
                    if (string.IsNullOrEmpty(targetNos.SourceClassGenericType))
                    {
                        canBeMovedInList = false;
                        //toReturn.Message = "The target Object has not been given a list type yet";
                    }
                    else if (movingNos.CanBeInList(targetNos) == false)
                    {
                        canBeMovedInList = false;
                        //toReturn.Message = "The Object you are moving is of type " + movingNos.SourceClassType +
                        //    " but the list is of type " + targetNos.SourceClassGenericType;

                    }
                    else if (treeNodeMoving.Parent.IsRootNamedObjectNode() == false)
                    {
                        canBeMovedInList = false;
                        //toReturn.Message = "The Object you are moving is already part of a list, so it can't be moved";
                    }
                    else
                    {
                        canBeMovedInList = true;
                    }
                }
            }

            if (canBeMovedInList && canBeCollidable)
            {
                string message = "Move to list or create collision relationship?";

                var mbmb = new MultiButtonMessageBoxWpf();
                mbmb.MessageText = message;
                mbmb.AddButton("Move to List", DialogResult.Yes);
                mbmb.AddButton("Create Collision Relationship", DialogResult.No);

                var dialogResult = mbmb.ShowDialog();

                if(dialogResult == true)
                {
                    var result = (DialogResult)mbmb.ClickedResult;
                    if ( result == DialogResult.Yes)
                    {
                        canBeMovedInList = true;
                        canBeCollidable = false;
                    }
                    else if (result == DialogResult.No)
                    {
                        canBeCollidable = true;
                        canBeMovedInList = false;
                    }
                }
            }

            if (canBeMovedInList)
            {
                var response = HandleDropOnList(treeNodeMoving, targetNode, targetNos, movingNos);
                if (!response.Succeeded)
                {
                    MessageBox.Show(response.Message);
                }
                succeeded = response.Succeeded;
            }
            else if (canBeCollidable)
            {
                var response = HandleCreateCollisionRelationship(movingNos, targetNos);

                if (!response.Succeeded)
                {
                    MessageBox.Show(response.Message);
                }

                succeeded = response.Succeeded;
            }

            if(succeeded)
            {
                GlueCommands.Self.RefreshCommands.RefreshTreeNodeFor(element);
                GlueState.Self.CurrentNamedObjectSave = movingNos;
            }

            return succeeded;
        }

        private GeneralResponse HandleCreateCollisionRelationship(NamedObjectSave movingNos, NamedObjectSave targetNos)
        {
            PluginManager.ReactToCreateCollisionRelationshipsBetween(movingNos, targetNos);
            return GeneralResponse.SuccessfulResponse;
        }

        // if both are lists, and both are ICollidable, then bring up the collision relationship 
        static bool IsCollidableOrCollidableList(NamedObjectSave nos)
        {
            if (nos.IsList)
            {
                var type = nos.SourceClassGenericType;

                // For a more complete impl, see:
                // CollisionRelationshipViewModelController
                return !string.IsNullOrEmpty(nos.SourceClassGenericType) &&
                    ObjectFinder.Self.GetEntitySave(nos.SourceClassGenericType)?.ImplementsICollidable == true;
            }
            else if(nos.GetAssetTypeInfo()?.RuntimeTypeName == "FlatRedBall.TileCollisions.TileShapeCollection" ||
                nos.GetAssetTypeInfo()?.RuntimeTypeName == "TileShapeCollection")
            {
                return true;
            }
            else if(nos.GetAssetTypeInfo()?.RuntimeTypeName == "FlatRedBall.Math.Geometry.ShapeCollection" ||
                nos.GetAssetTypeInfo()?.RuntimeTypeName == "ShapeCollection")
            {
                return true;
            }
            else if(nos.SourceType == SourceType.Entity && 
                ObjectFinder.Self.GetEntitySave( nos.SourceClassType)?.ImplementsICollidable == true)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private static bool MoveObjectOnObjectsRoot(ITreeNode treeNodeMoving, ITreeNode targetNode, NamedObjectSave movingNos, bool succeeded)
        {
            // Dropped it on the "Objects" tree node

            // Let's see if it's the Objects that contains node or another one

            var parentOfMovingNos = movingNos.GetContainer();
            var elementMovingInto = (targetNode.Parent).Tag as GlueElement;

            if (parentOfMovingNos == elementMovingInto)
            {

                if (treeNodeMoving.Parent.IsNamedObjectNode())
                {
                    succeeded = true;

                    // removing from a list
                    NamedObjectSave container = treeNodeMoving.Parent.Tag as NamedObjectSave;

                    var elementToAddTo = movingNos.GetContainer();
                    container.ContainedObjects.Remove(movingNos);
                    AddExistingNamedObjectToElement(
                        GlueState.Self.CurrentElement, movingNos);
                    GlueCommands.Self.RefreshCommands.RefreshTreeNodeFor(elementToAddTo);

                    IElement elementToRegenerate = targetNode.Parent.Tag as IElement;

                    PluginManager.ReactToObjectContainerChanged(movingNos, null);
                }
            }
            else
            {
                succeeded = DragDropNosIntoElement(movingNos, elementMovingInto);
            }
            return succeeded;
        }

        private static bool DragDropNosIntoElement(NamedObjectSave movingNos, GlueElement elementMovingInto)
        {
            var response = GlueCommands.Self.GluxCommands.CopyNamedObjectIntoElement(movingNos, elementMovingInto,
                save:false);

            if(response.Succeeded)
            {
                GlueCommands.Self.PrintOutput("Copied\n" + movingNos + "\n\nto\n" + elementMovingInto);
            }
            else
            {
                GlueCommands.Self.DialogCommands.ShowMessageBox(response.Message);
            }
            return response.Succeeded;
        }

        private static void UpdateNosAttachmentAfterDragDrop(NamedObjectSave clonedNos, IElement elementMovingInto)
        {
            if (elementMovingInto is EntitySave)
            {
                clonedNos.AttachToCamera = false;
                clonedNos.AttachToContainer = true;
            }
            else if (elementMovingInto is ScreenSave)
            {
                clonedNos.AttachToContainer = false;
            }
        }

        private static bool MoveObjectOnRootCustomVariablesNode(ITreeNode treeNodeMoving, ITreeNode targetNode)
        {
            bool succeeded = true;

            if (treeNodeMoving.GetContainingElementTreeNode() != targetNode.GetContainingElementTreeNode())
            {
                succeeded = false;
            }

            if (succeeded)
            {
                // show the add new variable window and select this object
                GlueCommands.Self.DialogCommands.ShowAddNewVariableDialog(
                    CustomVariableType.Tunneled, 
                    ((NamedObjectSave)treeNodeMoving.Tag).InstanceName);
            }

            return succeeded;
        }

        private static bool DragDropNosOnRootEventsNode(ITreeNode treeNodeMoving, ITreeNode targetNode)
        {
            bool succeeded = true;


            if (treeNodeMoving.GetContainingElementTreeNode() != targetNode.GetContainingElementTreeNode())
            {
                succeeded = false;
            }

            if (succeeded)
            {
                GlueCommands.Self.DialogCommands.ShowAddNewEventDialog(treeNodeMoving.Tag as NamedObjectSave);
            }

            return succeeded;
        }

        private static GeneralResponse HandleDropOnList(ITreeNode treeNodeMoving, ITreeNode targetNode, NamedObjectSave targetNos, NamedObjectSave movingNos)
        {
            var toReturn = GeneralResponse.SuccessfulResponse;


                toReturn.Succeeded = true;

                // Get the old parent of the moving NOS
                var parentTreeNode = treeNodeMoving.Parent;
                if (parentTreeNode.IsNamedObjectNode())
                {
                    NamedObjectSave parentNos = parentTreeNode.Tag as NamedObjectSave;

                    parentNos.ContainedObjects.Remove(movingNos);
                }
                else
                {
                    var elementToRemoveFrom = ObjectFinder.Self.GetElementContaining(movingNos);
                    elementToRemoveFrom?.NamedObjects.Remove(movingNos);
                }
                parentTreeNode.Remove(treeNodeMoving);
                targetNode.Add(treeNodeMoving);
                // Add the NOS to the tree node moving on
                targetNos.ContainedObjects.Add(movingNos);

                PluginManager.ReactToObjectContainerChanged(movingNos, targetNos);


            return toReturn;
        }

        private GeneralResponse HandleDropOnShapeCollection(ITreeNode treeNodeMoving, ITreeNode targetNode, NamedObjectSave targetNos, NamedObjectSave movingNos)
        {
            var toReturn = GeneralResponse.SuccessfulResponse;

            if(movingNos.CanBeInShapeCollection() == false)
            {
                toReturn.Succeeded = false;
                toReturn.Message = "The Object you are moving is of type " + movingNos.SourceClassType +
                    " which cannot be contained in a ShapeCollection";
            }
            else
            {
                var parentTreeNode = treeNodeMoving.Parent;
                if (parentTreeNode.IsNamedObjectNode())
                {
                    NamedObjectSave parentNos = parentTreeNode.Tag as NamedObjectSave;

                    parentNos.ContainedObjects.Remove(movingNos);
                }
                else
                {
                    var element = ObjectFinder.Self.GetElementContaining(movingNos);
                    element.NamedObjects.Remove(movingNos);
                }
                parentTreeNode.Remove(treeNodeMoving);
                targetNode.Add(treeNodeMoving);
                targetNos.ContainedObjects.Add(movingNos);

                PluginManager.ReactToObjectContainerChanged(movingNos, targetNos);
            }
            return toReturn;
        }

        private static void AddExistingNamedObjectToElement(GlueElement element, NamedObjectSave newNamedObject)
        {
            element.NamedObjects.Add(newNamedObject);
            GlueCommands.Self.RefreshCommands.RefreshTreeNodeFor(element);
            GlueCommands.Self.GenerateCodeCommands.GenerateElementCode(element);

            // run after generated code so plugins like level editor work off latest code
            PluginManager.ReactToNewObject(newNamedObject);

        }

        #endregion

        #region Custom Variable

        private static void MoveCustomVariable(ITreeNode nodeMoving, ITreeNode targetNode)
        {
            CustomVariable customVariable = nodeMoving.Tag as CustomVariable;

            if (targetNode.IsRootEventsNode())
            {
                // The user dragged a variable onto the events node, so they want to make
                // an event for this.  We'll assume an "after" event since I think no one makes
                // before events


                if (customVariable != null)
                {
                    customVariable.CreatesEvent = true;

                    FlatRedBall.Glue.Events.EventResponseSave eventResponseSave = new Events.EventResponseSave();
                    eventResponseSave.EventName = "After" + customVariable.Name + "Set";

                    eventResponseSave.SourceObject = null;
                    eventResponseSave.SourceObjectEvent = null;

                    eventResponseSave.SourceVariable = customVariable.Name;
                    eventResponseSave.BeforeOrAfter = BeforeOrAfter.After;

                    eventResponseSave.DelegateType = null;

                    var element = targetNode.GetContainingElementTreeNode()?.Tag as GlueElement;
                    RightClickHelper.AddEventToElementAndSave(element, eventResponseSave);

                }
            }
            else if (targetNode.IsRootCustomVariablesNode())
            {
                MoveVariableOnVariablesRootNode(nodeMoving, targetNode);
            }
            else if(targetNode.IsStateCategoryNode())
            {
                MoveVariableOnStateCategory(nodeMoving.Tag as CustomVariable, targetNode.Tag as StateSaveCategory);
            }
        }

        private static void MoveVariableOnVariablesRootNode(ITreeNode nodeMoving, ITreeNode targetNode)
        {
            CustomVariable customVariable = nodeMoving.Tag as CustomVariable;

            // let's see if the user is moving a variable from one element to another
            var sourceElement = nodeMoving.GetContainingElementTreeNode().Tag as GlueElement;
            var targetElement = targetNode.GetContainingElementTreeNode().Tag as GlueElement;

            if (sourceElement != targetElement)
            {
                // copying a variable from one element to another
                // eventually we need to add some error checking here.
                CustomVariable newVariable = customVariable.Clone();

                targetElement.CustomVariables.Add(newVariable);


                GlueCommands.Self.GenerateCodeCommands.GenerateElementCode(targetElement);
                GlueCommands.Self.RefreshCommands.RefreshTreeNodeFor(targetElement);
            }
        }

        private static void MoveVariableOnStateCategory(CustomVariable customVariable, StateSaveCategory stateSaveCategory)
        {
            TaskManager.Self.AddOrRunIfTasked(() =>
            {
                if (stateSaveCategory.ExcludedVariables.Contains(customVariable.Name))
                {
                    stateSaveCategory.ExcludedVariables.Remove(customVariable.Name);
                }
                var container = ObjectFinder.Self.GetElementContaining(stateSaveCategory);

                PluginManager.ReactToStateCategoryExcludedVariablesChangedAsync(stateSaveCategory, customVariable.Name, StateCategoryVariableAction.Included);

                GlueCommands.Self.GenerateCodeCommands.GenerateElementCode(container);
                GlueCommands.Self.RefreshCommands.RefreshTreeNodeFor(container);
                GlueCommands.Self.GluxCommands.SaveGlux();

                GlueCommands.Self.PrintOutput($"Including variable {customVariable.Name} in category {stateSaveCategory.Name}");
            }, $"Including variable {customVariable.Name} in category {stateSaveCategory.Name}", TaskExecutionPreference.Asap);
        }


        #endregion

        #region StateSave

        internal void MoveState(ITreeNode nodeMoving, ITreeNode targetNode)
        {

            var currentElement = targetNode.GetContainingElementTreeNode()?.Tag as GlueElement;
            var currentState = nodeMoving.Tag as StateSave;

            if(currentElement== null || currentState == null)
            {
                return;
            }

            StateSave toAdd = (StateSave)nodeMoving.Tag;

            var sourceContainer = nodeMoving.GetContainingElementTreeNode().Tag as GlueElement;
            var targetContainer = targetNode.GetContainingElementTreeNode().Tag as GlueElement;


            if (targetNode.IsStateCategoryNode() || targetNode.IsRootStateNode())
            {
                if (sourceContainer == targetContainer)
                {
                    currentElement.RemoveState(currentState);
                }
                else
                {
                    toAdd = toAdd.Clone();
                }

                if (targetNode.IsStateCategoryNode())
                {
                    ((StateSaveCategory)targetNode.Tag).States.Add(toAdd);
                }
                else
                {
                    targetContainer.States.Add(toAdd);
                }

                InheritanceManager.UpdateAllDerivedElementFromBaseValues(true, currentElement);

                GlueCommands.Self.GenerateCodeCommands.GenerateElementCode(targetContainer);
                GlueCommands.Self.RefreshCommands.RefreshTreeNodeFor(targetContainer);

            }


        }

        #endregion

        #region StateSaveCategory

        internal void MoveStateCategory(ITreeNode nodeMoving, ITreeNode targetNode)
        {
            if (targetNode.IsRootCustomVariablesNode() || targetNode.IsCustomVariable())
            {
                // The user drag+dropped a state category into the variables
                // Let's make sure that it's all in the same Element though:
                // Update December 30, 2021 - Glue now supports variables which
                // are a state category which comes from a different entity/screen.
                // I don't want to uncomment this right now because that may require
                // some additional testing, but I'm putting this comment here so that
                // in the future it's clear that this was an old rule which could be removed
                // with proper testing.
                if (targetNode.GetContainingElementTreeNode() == nodeMoving.GetContainingElementTreeNode())
                {
                    StateSaveCategory category = nodeMoving.Tag as StateSaveCategory;

                    // expose a variable that exposes the category
                    CustomVariable customVariable = new CustomVariable();

                    customVariable.Type = category.Name;
                    customVariable.Name = "Current" + category.Name + "State";

                    var element = targetNode.GetContainingElementTreeNode().Tag as GlueElement;

                    GlueCommands.Self.GluxCommands.ElementCommands.AddCustomVariableToElement(
                        customVariable, element);
                }
            }
        }

        #endregion

        #region Entity

        public async Task<ITreeNode> MoveEntityOn(ITreeNode treeNodeMoving, ITreeNode targetNode)
        {
            ITreeNode newTreeNode = null;

            #region Moving the Entity into (or out of) a directory
            if (targetNode.IsDirectoryNode() || targetNode.IsRootEntityNode())
            {
                MoveEntityToDirectory(treeNodeMoving, targetNode);
            }

            #endregion

            #region Moving an Entity onto another element to create an instance

            else if (targetNode.IsEntityNode() || targetNode.IsScreenNode() || targetNode.IsRootNamedObjectNode())
            {
                bool isValidDrop = true;
                // Make sure that we don't drop an Entity into its own Objects
                if (targetNode.IsRootNamedObjectNode())
                {
                    if (treeNodeMoving == targetNode.GetContainingElementTreeNode())
                    {
                        isValidDrop = false;
                    }
                }

                // If the entity has any SetByDerived objects, then it's abstract so don't allow it!
                var entity = treeNodeMoving.Tag as EntitySave;
                var isAbstract = entity.AllNamedObjects.Any(item => item.SetByDerived);
                if(isAbstract)
                {
                    GlueCommands.Self.DialogCommands.ShowMessageBox($"Cannot add {entity} to {targetNode.Text} because it is an abstract class");
                    isValidDrop = false;
                }
                
                if (isValidDrop)
                {
                    newTreeNode = await DropEntityOntoElement(treeNodeMoving.Tag as EntitySave, targetNode.GetContainingElementTreeNode().Tag as GlueElement);
                }
            }

            #endregion

            #region Moving an Entity onto a NamedObject (Lists, layers)

            else if (targetNode.IsNamedObjectNode())
            {
                newTreeNode = await MoveEntityOnNamedObject (treeNodeMoving, targetNode);
            }

            #endregion

            else if (targetNode.IsGlobalContentContainerNode())
            {
                AskAndAddAllContainedRfsToGlobalContent(treeNodeMoving.Tag as IElement);
            }

            return newTreeNode;
        }

        private async Task<ITreeNode> MoveEntityOnNamedObject(ITreeNode treeNodeMoving, ITreeNode targetNode)
        {
            // todo - this should be in a task

            ITreeNode newTreeNode = null;
            var entity = treeNodeMoving.Tag as EntitySave;

            var targetElement = targetNode.GetContainingElementTreeNode()?.Tag as GlueElement;

            // Allow drop only if it's a list or Layer
            NamedObjectSave targetNamedObjectSave = targetNode.Tag as NamedObjectSave;

            if (!targetNamedObjectSave.IsList && !targetNamedObjectSave.IsLayer)
            {
                MessageBox.Show("The target is not a List or Layer so we can't add an Object to it.", "Target not valid");
            }
            if (targetNamedObjectSave.IsLayer)
            {
                var parent = targetNode.Parent;

                newTreeNode = await MoveEntityOn(treeNodeMoving, parent);

                DragDropManager.Self.MoveNamedObject(newTreeNode, targetNode);
            }
            else
            {
                // Make sure that the two types match
                string listType = targetNamedObjectSave.SourceClassGenericType;

                bool isOfTypeOrInherits =
                    listType == entity.Name ||
                    entity.InheritsFrom(listType);

                if (isOfTypeOrInherits == false)
                {
                    MessageBox.Show("The target list type is of type\n\n" +
                        listType +
                        "\n\nBut the Entity is of type\n\n" +
                        entity.Name +
                        "\n\nCould not add an instance to the list", "Could not add instance");
                }
                else
                {
                    var namedObject = new NamedObjectSave();

                    if (GlueState.Self.CurrentGlueProject.FileVersion >=
                        (int)GlueProjectSave.GluxVersions.ListsHaveAssociateWithFactoryBool)
                    {
                        namedObject.AssociateWithFactory = true;
                    }
                    namedObject.InstanceName =
                        FileManager.RemovePath(entity.Name) + "1";

                    StringFunctions.MakeNameUnique<NamedObjectSave>(
                        namedObject, targetNamedObjectSave.ContainedObjects);

                    // Not sure if we need to set this or not, but I think 
                    // any instance added to a list will not be defined by base
                    namedObject.DefinedByBase = false;

                    // make sure that the target list is the current
                    GlueState.Self.CurrentNamedObjectSave = targetNamedObjectSave;

                    var currentNosList = GlueState.Self.CurrentNamedObjectSave;

                    NamedObjectSaveExtensionMethodsGlue.AddNamedObjectToList(namedObject,
                        currentNosList);

                    // If the tree node doesn't exist yet, this selection won't work:
                    GlueCommands.Self.RefreshCommands.RefreshTreeNodeFor(
                        targetElement);

                    GlueState.Self.CurrentNamedObjectSave = namedObject;

                    if (namedObject.SourceClassType != entity.Name)
                    {
                        namedObject.SourceClassType = entity.Name;
                        namedObject.UpdateCustomProperties();
                    }

                    GlueCommands.Self.GenerateCodeCommands.GenerateElementCode(targetElement);

                    // run after generated code so plugins like level editor work off latest code
                    PluginManager.ReactToNewObject(namedObject);
                    PluginManager.ReactToObjectContainerChanged(namedObject, currentNosList);

                    // Don't save the Glux, the caller of this method will take care of it
                    // GluxCommands.Self.SaveGlux();
                    newTreeNode = GlueState.Self.Find.TreeNodeByTag(namedObject);
                }

            }

            return newTreeNode;
        }

        static void MoveEntityToDirectory(ITreeNode treeNodeMoving, ITreeNode targetNode)
        {
            bool succeeded = true;

            EntitySave entitySave = treeNodeMoving.Tag as EntitySave;

            string newRelativeDirectory = targetNode.GetRelativePath();

            string newName = newRelativeDirectory.Replace("/", "\\") + entitySave.ClassName;

            // modify data and files
            succeeded = GlueCommands.Self.GluxCommands.MoveEntityToDirectory(entitySave, newRelativeDirectory);

            //// adjust the UI
            // Update November 14, 2021 - RefreshTreeNodeFor should handle this
            //if (succeeded)
            //{
            //    treeNodeMoving.Parent.Remove(treeNodeMoving);
            //    targetNode.Add(treeNodeMoving);
            //}

            // Generate and save
            if (succeeded)
            {
                GlueCommands.Self.RefreshCommands.RefreshTreeNodeFor(entitySave);

                GlueCommands.Self.ProjectCommands.MakeGeneratedCodeItemsNested();
                CodeWriter.GenerateCode(entitySave);

                GluxCommands.Self.SaveGlux();
                GlueCommands.Self.ProjectCommands.SaveProjects();

                GlueState.Self.CurrentElement = entitySave;
            }
        }

        public async Task<ITreeNode> DropEntityOntoElement(EntitySave entitySaveMoved, GlueElement elementToCreateIn)
        {
            ITreeNode newTreeNode = null;

            var listOfThisType = ObjectFinder.Self.GetDefaultListToContain(entitySaveMoved, elementToCreateIn);

            if(listOfThisType != null)
            {

                var namedObjectNode = GlueState.Self.Find.TreeNodeByTag(listOfThisType);
                var entityTreeNode = GlueState.Self.Find.TreeNodeByTag(entitySaveMoved);
                // move it onto this
                newTreeNode = await MoveEntityOn(entityTreeNode, namedObjectNode);
            }
            else
            {

                // We used to ask the user if they're sure, but this isn't a destructive action so just do it:
                //DialogResult result =
                //    MessageBox.Show("Create a new Object in\n\n" + elementToCreateIn.Name + "\n\nusing\n\n\t" + entitySaveMoved.Name + "?", "Create new Object?", MessageBoxButtons.YesNo);

                var newNamedObject = await CreateNewNamedObjectInElement(elementToCreateIn, entitySaveMoved);
                newTreeNode = GlueState.Self.Find.NamedObjectTreeNode(newNamedObject);
                GlueState.Self.CurrentNamedObjectSave = newNamedObject;
            }
            return newTreeNode;
        }

        public async Task<NamedObjectSave> CreateNewNamedObjectInElement(IElement elementToCreateIn, 
            EntitySave blueprintEntity, bool createList = false)
        {
            if (blueprintEntity == null)
            {
                throw new ArgumentNullException($"{nameof(blueprintEntity)} cannot be null");
            }

            if (elementToCreateIn is EntitySave && ((EntitySave)elementToCreateIn).ImplementsIVisible && !blueprintEntity.ImplementsIVisible)
            {
                var mbmb = new MultiButtonMessageBoxWpf();
                mbmb.MessageText = "The Entity\n\n" + blueprintEntity +
                    "\n\nDoes not Implement IVisible, but the Entity it is being dropped in does.  " +
                    "What would you like to do?";

                mbmb.AddButton("Make " + blueprintEntity.Name + " implement IVisible", DialogResult.OK);
                mbmb.AddButton("Nothing (your code will not compile until this problem is resolved manually)", DialogResult.Cancel);

                var dialogResult = mbmb.ShowDialog();

                DialogResult result = DialogResult.Cancel;

                if (mbmb.ClickedResult != null && dialogResult == true)
                {
                    result = (DialogResult)mbmb.ClickedResult;
                }

                if (result == DialogResult.OK)
                {
                    blueprintEntity.ImplementsIVisible = true;
                    GlueCommands.Self.GenerateCodeCommands
                        .GenerateElementAndReferencedObjectCode(blueprintEntity);
                }

            }

            var addObjectViewModel = new AddObjectViewModel();
            // We'll add "List" or "Instance" below
            //string newName = FileManager.RemovePath(blueprintEntity.Name);

            #region Set the source type properties for the new NamedObject

            if (createList)
            {
                addObjectViewModel.SourceType = SourceType.FlatRedBallType;

                addObjectViewModel.SelectedAti = AvailableAssetTypes.CommonAtis.PositionedObjectList;

                addObjectViewModel.SourceClassGenericType = blueprintEntity.Name;
                addObjectViewModel.ObjectName = FileManager.RemovePath(blueprintEntity.Name);
                addObjectViewModel.ObjectName += "List";
            }
            else
            {
                addObjectViewModel.SourceType = SourceType.Entity;
                addObjectViewModel.SelectedEntitySave = blueprintEntity;
                addObjectViewModel.ObjectName = FileManager.RemovePath(blueprintEntity.Name);
                addObjectViewModel.ObjectName += "Instance";
            }

            #endregion

            #region Set the name for the new NamedObject

            addObjectViewModel.ObjectName = IncrementNumberAtEndOfNewObject(elementToCreateIn, addObjectViewModel.ObjectName);


            #endregion

            return await GlueCommands.Self.GluxCommands.AddNewNamedObjectToAsync(addObjectViewModel,
                elementToCreateIn as GlueElement, null);
        }

        private static string IncrementNumberAtEndOfNewObject(IElement elementToCreateIn, string objectName)
        {
            // get an acceptable name for the new object
            if (elementToCreateIn.GetNamedObjectRecursively(objectName) != null)
            {
                objectName += "2";
            }

            while (elementToCreateIn.GetNamedObjectRecursively(objectName) != null)
            {
                objectName = StringFunctions.IncrementNumberAtEnd(objectName);
            }
            return objectName;
        }

        private static void AskAndAddAllContainedRfsToGlobalContent(IElement element)
        {
            string message = "Add all contained files in " + element.ToString() + " to Global Content Files?  Files will still be referenced by " + element.ToString();

            DialogResult dialogResult = MessageBox.Show(message, "Add to Global Content?", MessageBoxButtons.YesNo);

            if (dialogResult == DialogResult.Yes)
            {

                if (!element.UseGlobalContent)
                {
                    string screenOrEntity = "Screen";

                    if (element is EntitySave)
                    {
                        screenOrEntity = "Entity";
                    }

                    DialogResult result = MessageBox.Show("The " + screenOrEntity + " " + element.ToString() +
                        "does not UseGlobalContent.  Would you like " +
                        " to set UseGlobalContent to true?", "Set UseGlobalContent to true?", MessageBoxButtons.YesNo);

                    if (result == DialogResult.Yes)
                    {
                        element.UseGlobalContent = true;
                    }
                }

                foreach (ReferencedFileSave rfs in element.ReferencedFiles)
                {
                    bool alreadyExists = false;
                    foreach (ReferencedFileSave existingRfs in ObjectFinder.Self.GlueProject.GlobalFiles)
                    {
                        if (existingRfs.Name.ToLower() == rfs.Name.ToLower())
                        {
                            alreadyExists = true;
                            break;
                        }
                    }

                    if (!alreadyExists)
                    {
                        bool useFullPathAsName = true;
                        GlueCommands.Self.GluxCommands.AddReferencedFileToGlobalContent(rfs.Name, useFullPathAsName);
                    }
                }


                GlobalContentCodeGenerator.UpdateLoadGlobalContentCode();

                GlueCommands.Self.ProjectCommands.SaveProjects();
                GluxCommands.Self.SaveGlux();
            }
        }

        #endregion

        #region Referenced File

        public void MoveReferencedFile(ITreeNode treeNodeMoving, ITreeNode targetNode)
        {
            var response = GeneralResponse.SuccessfulResponse;

            while (targetNode != null && targetNode.IsReferencedFile())
            {
                targetNode = targetNode.Parent;
            }
            // If the user drops a file on a Screen or Entity, let's allow them to
            // complete the operation on the Files node
            if (targetNode.Tag is GlueElement)
            {
                targetNode = targetNode.FindByName("Files");
            }

            ReferencedFileSave referencedFileSave = treeNodeMoving.Tag as ReferencedFileSave;

            if (!targetNode.IsFilesContainerNode() &&
                !targetNode.IsFolderInFilesContainerNode() &&
                !targetNode.IsFolderForGlobalContentFiles() &&
                !targetNode.IsNamedObjectNode() &&
                !targetNode.IsRootNamedObjectNode() &&
                !targetNode.IsGlobalContentContainerNode())
            {
                response.Fail(@"Can't drop this file here");
            }
            else if (!string.IsNullOrEmpty(referencedFileSave.SourceFile) ||
                referencedFileSave.SourceFileCache?.Count > 0)
            {
                response.Fail("Can't move the file\n\n" + referencedFileSave.Name + "\n\nbecause it has source-referencing files.  These sources will be broken " +
                    "if the file is moved.  You will need to manually move the file, modify the source references, remove this file, then add the newly-created file.");
            }

            if (response.Succeeded)
            {

                if (targetNode.IsGlobalContentContainerNode())
                {
                    if (targetNode.GetContainingElementTreeNode() == null)
                    {
                        string targetDirectory = ProjectManager.MakeAbsolute(targetNode.GetRelativePath(), true);
                        MoveReferencedFileToDirectory(referencedFileSave, targetDirectory);
                    }
                    else
                    {
                        DragAddFileToGlobalContent(referencedFileSave);
                        // This means the user wants to add the file
                        // to global content.
                    }
                }
                else if (targetNode.IsFolderForGlobalContentFiles())
                {
                    string targetDirectory = ProjectManager.MakeAbsolute(targetNode.GetRelativePath(), true);
                    MoveReferencedFileToDirectory(referencedFileSave, targetDirectory);
                }
                else if (targetNode.IsRootNamedObjectNode())
                {
                    AddObjectViewModel viewModel = new AddObjectViewModel();
                    viewModel.SourceType = SourceType.File;
                    viewModel.SourceFile = (treeNodeMoving.Tag as ReferencedFileSave);
                    GlueCommands.Self.DialogCommands.ShowAddNewObjectDialog(viewModel);
                }
                else if (targetNode.IsNamedObjectNode() &&
                    // dropping on an object in the same element
                    targetNode.GetContainingElementTreeNode() == treeNodeMoving.GetContainingElementTreeNode())
                {
                    response = HandleDroppingFileOnObjectInSameElement(targetNode, referencedFileSave);

                }

                //if (targetNode.IsFolderInFilesContainerNode() || targetNode.IsFilesContainerNode())
                else
                {
                    // See if we're moving the RFS from one Element to another
                    IElement container = ObjectFinder.Self.GetElementContaining(referencedFileSave);
                    var elementTreeNodeDroppingIn = targetNode.GetContainingElementTreeNode();
                    GlueElement elementDroppingIn = null;
                    if (elementTreeNodeDroppingIn != null)
                    {
                        // User didn't drop on an entity, but instead on a node within the entity.
                        // Let's check if it's a subfolder. If so, we need to tell the user that we
                        // can't add the file in a subfolder.

                        if (targetNode.IsFolderInFilesContainerNode())
                        {
                            response.Message = "Shared files cannot be added to subfolders, so it will be added directly to \"Files\"";
                        }

                        elementDroppingIn = elementTreeNodeDroppingIn.Tag as GlueElement;
                    }

                    if (container != elementDroppingIn)
                    {
                        // Make sure the target element is not named the same as the file itself.
                        // For example, dropping a file called Level1.tmx in a screen called Level1. 
                        // This will not compile so we shouldn't allow it.

                        var areNamedTheSame = elementDroppingIn.GetStrippedName() == referencedFileSave.GetInstanceName();

                        if (areNamedTheSame)
                        {
                            response.Fail($"The file {referencedFileSave.GetInstanceName()} has the same name as the target screen. it will not be added since this is not allowed.");
                        }

                        if (response.Succeeded)
                        {

                            GlueState.Self.CurrentReferencedFileSave = referencedFileSave;

                            string absoluteFileName = ProjectManager.MakeAbsolute(referencedFileSave.Name, true);
                            string creationReport;
                            string errorMessage;

                            var newlyCreatedFile = ElementCommands.Self.CreateReferencedFileSaveForExistingFile(elementDroppingIn, null, absoluteFileName,
                                                                            PromptHandleEnum.Prompt,
                                                                            referencedFileSave.GetAssetTypeInfo(),
                                                                            out creationReport,
                                                                            out errorMessage);

                            if(elementDroppingIn != null)
                            {
                                GlueCommands.Self.RefreshCommands.RefreshTreeNodeFor(elementDroppingIn);
                                GlueCommands.Self.GenerateCodeCommands.GenerateElementCode(elementDroppingIn);
                            }
                            else
                            {
                                GlueCommands.Self.RefreshCommands.RefreshGlobalContent();
                                GlueCommands.Self.GenerateCodeCommands.GenerateGlobalContentCode();
                            }

                            if (!String.IsNullOrEmpty(errorMessage))
                            {
                                MessageBox.Show(errorMessage);
                            }
                            else if (newlyCreatedFile != null)
                            {
                                GlueState.Self.CurrentReferencedFileSave = newlyCreatedFile;
                            }
                        }
                    }
                    else
                    {
                        var targetElementContentFolder = new FilePath( GlueCommands.Self.FileCommands.GetContentFolder(elementDroppingIn));

                        var fileAbsolutePath = GlueCommands.Self.GetAbsoluteFilePath(referencedFileSave);

                        var isRelativeToElementBeforeMove = targetElementContentFolder.IsRootOf(fileAbsolutePath);

                        if(isRelativeToElementBeforeMove)
                        {
                            string targetDirectory = ProjectManager.MakeAbsolute(targetNode.GetRelativePath(), true);
                            MoveReferencedFileToDirectory(referencedFileSave, targetDirectory);
                        }
                        else
                        {
                            GlueCommands.Self.PrintOutput($"Could not move {referencedFileSave} because it is not inside the content folder for {elementDroppingIn}");

                        }

                    }
                }
            }

            if (!string.IsNullOrEmpty(response.Message))
            {
                MessageBox.Show(response.Message);
            }
        }

        private static void DragAddFileToGlobalContent(ReferencedFileSave referencedFileSave)
        {
            if (referencedFileSave.GetContainerType() == ContainerType.None)
            {
                // This means the user dragged a file from global content onto the global content tree node - 
                // we shouldn't do anything here.  It's not a valid operation, but at the same time, it may have
                // happened accidentally and we don't want to burden the user with popups.
            }
            else
            {
                bool isAlreadyPartOfReferencedFiles = false;
                // If the file is already part of GlobalContent, then warn the user and do nothing
                foreach (ReferencedFileSave fileInGlobalContent in ObjectFinder.Self.GlueProject.GlobalFiles)
                {
                    if (fileInGlobalContent.Name == referencedFileSave.Name)
                    {
                        isAlreadyPartOfReferencedFiles = true;
                        break;
                    }
                }


                if (isAlreadyPartOfReferencedFiles)
                {

                    MessageBox.Show("The file\n\n" + referencedFileSave.Name + "\n\nis already a Global Content File");
                }
                else
                {
                    // If we got here, that means that the file that
                    // the user is dragging in to Global Content Files
                    // can be added to Global Content Files; however, the
                    // owner of the file may not be using global content.  We
                    // should ask the user if the containing IElement should use
                    // global content
                    IElement container = referencedFileSave.GetContainer();



                    if (!container.UseGlobalContent)
                    {
                        string screenOrEntity = "Screen";

                        if (container is EntitySave)
                        {
                            screenOrEntity = "Entity";
                        }

                        DialogResult result = MessageBox.Show("The " + screenOrEntity + " " + container.ToString() +
                            "does not UseGlobalContent.  Would you like " +
                            " to set UseGlobalContent to true?", "Set UseGlobalContent to true?", MessageBoxButtons.YesNo);

                        if (result == DialogResult.Yes)
                        {
                            container.UseGlobalContent = true;
                        }
                    }

                    bool useFullPathAsName = true;
                    GlueCommands.Self.GluxCommands.AddReferencedFileToGlobalContent(referencedFileSave.Name, useFullPathAsName);

                    GlobalContentCodeGenerator.UpdateLoadGlobalContentCode();

                    GlueCommands.Self.ProjectCommands.SaveProjects();
                    GluxCommands.Self.SaveGlux();
                }

            }

        }

        private static void MoveReferencedFileToDirectory(ReferencedFileSave referencedFileSave, string targetDirectory)
        {
            // Things to do:
            // 1 Move the TreeNode from one parent TreeNode to another UPDATE:  We will just refresh the UI for the Element or GlobalContent
            // 2 Move the file from one folder to another
            // 3 Remove the BuildItems from the project and add them back in the VisualStudio project
            // 4 Change the ReferencedFileSave's name
            // 5 Re-generate the containing Element (Screen or Entity)
            // 6 Save everything

            string oldNodeText = referencedFileSave.Name.Replace("/", "\\");



            string newNodeText = FlatRedBall.IO.FileManager.MakeRelative(targetDirectory, ProjectManager.ProjectBase.GetAbsoluteContentFolder()) + FileManager.RemovePath(referencedFileSave.Name);
            newNodeText = newNodeText.Replace("/", "\\");

            string oldFileName = ProjectManager.MakeAbsolute(referencedFileSave.Name, true);
            string targetFile = targetDirectory + FileManager.RemovePath(oldFileName);
            var elementContainingMovedFile = ObjectFinder.Self.GetElementContaining(referencedFileSave);
            bool canMove = true;

            // There's so much error checking and validation that we
            // could/should do here, but man, I just can't spend forever
            // on it because I need to get the game I'm working on moving forward
            // But I'm going to at least improve it a little bit by having the referenced
            // files get copied over.
            Dictionary<string, string> mOldNewDependencyFileDictionary = new Dictionary<string, string>();
            var referencedFiles = ContentParser.GetFilesReferencedByAsset(oldFileName, TopLevelOrRecursive.Recursive);
            string oldDirectoryFull = FileManager.GetDirectory(oldFileName);

            foreach (var file in referencedFiles)
            {
                string relativeToRfs = FileManager.MakeRelative(file.FullPath, FileManager.GetDirectory(oldFileName));

                string targetReferencedFileName = targetDirectory + relativeToRfs;

                mOldNewDependencyFileDictionary.Add(file.FullPath, targetReferencedFileName);

                if (!FileManager.IsRelativeTo(targetReferencedFileName, targetDirectory))
                {
                    MessageBox.Show("The file\n\n" + file + "\n\nis not relative to the file being moved, so it cannot be moved.  You must manually move these files and manually update the file reference.");
                    canMove = false;
                    break;
                }
            }


            if (canMove && File.Exists(targetFile))
            {
                MessageBox.Show("There is already a file by this name located in the directory you're trying to move to.");
                canMove = false;
            }
            if (canMove)
            {
                foreach (KeyValuePair<string, string> kvp in mOldNewDependencyFileDictionary)
                {
                    if (File.Exists(kvp.Value))
                    {
                        MessageBox.Show("Can't move the file because a dependency will be moved to\n\n" + kvp.Value + "\n\nand a file already exists there.");
                        canMove = false;
                        break;
                    }
                }

            }

            if (canMove)
            {
                // 1 Move the TreeNode from one parent TreeNode to another            
                //treeNodeMoving.Parent.Nodes.Remove(treeNodeMoving);
                //targetNode.Nodes.Add(treeNodeMoving);
                // This is updated at the bottom of this method


                // In case it doesn't exist
                System.IO.Directory.CreateDirectory(FileManager.GetDirectory(targetFile));

                // 2 Move the file from one folder to another
                File.Move(oldFileName, targetFile);
                foreach (KeyValuePair<string, string> kvp in mOldNewDependencyFileDictionary)
                {
                    File.Move(kvp.Key, kvp.Value);
                }


                // 3 Remove the BuildItems from the project and add them back in the VisualStudio project
                var projectBase = ProjectManager.ProjectBase;
                if (ProjectManager.ContentProject != null)
                {
                    projectBase = ProjectManager.ContentProject;
                }

                var oldFileRelativeToProject = FileManager.MakeRelative(oldFileName, projectBase.Directory);
                ProjectManager.RemoveItemFromProject(projectBase, oldFileRelativeToProject, false);
                projectBase.AddContentBuildItem(targetFile);
                foreach (KeyValuePair<string, string> kvp in mOldNewDependencyFileDictionary)
                {
                    string fileFileRelativeToProject = FileManager.MakeRelative(kvp.Key, projectBase.Directory);

                    ProjectManager.RemoveItemFromProject(projectBase, fileFileRelativeToProject, false);
                    projectBase.AddContentBuildItem(kvp.Value);
                }
                // TODO:  This should also check to see if something else is referencing this content.
                // I'm going to write it to not make this check now since I'm just getting the initial system set up



                // 4 Change the ReferencedFileSave's name
                referencedFileSave.SetNameNoCall(newNodeText.Replace("\\", "/"));
                // No need for this, it'll get updated automatically
                // treeNodeMoving.Text = newNodeText;



                // 5 Re-generate the containing Element (Screen or Entity)
                if (elementContainingMovedFile != null)
                {
                    CodeWriter.GenerateCode(elementContainingMovedFile);
                }
                else
                {
                    GlobalContentCodeGenerator.UpdateLoadGlobalContentCode();
                }


                // The new 1:  Update 
                if (elementContainingMovedFile != null)
                {
                    GlueCommands.Self.RefreshCommands.RefreshTreeNodeFor(elementContainingMovedFile);
                }
                else
                {
                    GlueCommands.Self.RefreshCommands.RefreshGlobalContent();
                }


                // 6 Save everything
                GluxCommands.Self.SaveGlux();
                GlueCommands.Self.ProjectCommands.SaveProjects();
            }
        }

        private static GeneralResponse HandleDroppingFileOnObjectInSameElement(ITreeNode targetNode, ReferencedFileSave referencedFileSave)
        {
            var namedObject = (NamedObjectSave)targetNode.Tag;

            var response = GeneralResponse.SuccessfulResponse;

            var handled = TrySetNosToBeCreatedFromFile(referencedFileSave, namedObject);

            if(!handled)
            {
                handled = TrySetVariableOnNos(referencedFileSave, namedObject);
            }

            if(!handled)
            {
                response.Fail(
                    $"The object {namedObject.InstanceName} cannot be entirely set from {referencedFileSave.Name}." +
                    $"To set this object from an object contained within the file, select the object and change its source values.");
            }

            return response;
        }

        private static bool TrySetVariableOnNos(ReferencedFileSave referencedFileSave, NamedObjectSave namedObject)
        {
            var nosAti = namedObject.GetAssetTypeInfo();
            var fileAti = referencedFileSave.GetAssetTypeInfo();

            ////////////Early Out/////////////
            if(nosAti == null || fileAti == null)
            {
                return false;
            }
            //////////End Early Out////////////

            var availableVariables = nosAti.VariableDefinitions;

            var matchingVariable = availableVariables.FirstOrDefault(item => item.Type == fileAti.QualifiedRuntimeTypeName.QualifiedType);

            if(matchingVariable == null)
            {
                matchingVariable = availableVariables.FirstOrDefault(item => item.Type == fileAti.RuntimeTypeName);
            }

            if(matchingVariable != null)
            {
                GlueCommands.Self.GluxCommands.SetVariableOn(namedObject, matchingVariable.Name, FileManager.RemovePath(FileManager.RemoveExtension( referencedFileSave.Name)) );


                GlueCommands.Self.GluxCommands.SaveGlux();

                var element = ObjectFinder.Self.GetElementContaining(namedObject);
                if(element != null)
                {
                    GlueCommands.Self.GenerateCodeCommands.GenerateElementCode(element);
                }

                return true;
            }

            return false;
        }

        private static bool TrySetNosToBeCreatedFromFile(ReferencedFileSave referencedFileSave, NamedObjectSave namedObject)
        {
            // Dropping the file on an object. If the object's type matches the named object's
            // entire file, ask the user if they want to make the object come from the file...
            var namedObjectAti = namedObject.GetAssetTypeInfo();
            AssetTypeInfo rfsAti = referencedFileSave.GetAssetTypeInfo();
            var element = ObjectFinder.Self.GetElementContaining(referencedFileSave);

            var doFileAndNamedObjectHaveMatchingTypes =
                rfsAti == namedObjectAti && rfsAti != null;

            /////////////////Early Out////////////////////////
            ///
            if(!doFileAndNamedObjectHaveMatchingTypes)
            {
                return false;
            }
            //////////////End Early Out///////////////////////

            var dialogResult = MessageBox.Show(
                                $"Would you like to set the object {namedObject.InstanceName} to be created from the file {referencedFileSave.Name}?",
                                $"Set {namedObject.InstanceName} to be from file?",
                                MessageBoxButtons.YesNo);

            if (dialogResult == DialogResult.Yes)
            {
                namedObject.SourceType = SourceType.File;
                namedObject.SourceFile = referencedFileSave.Name;
                namedObject.SourceName = $"Entire File ({rfsAti.RuntimeTypeName})";

                // This might be the case if the base is SetbyDerived. 
                if (namedObject.Instantiate == false)
                {
                    // If an entire object is dropped on this, it's likely that the user wants to create (aka instantiate)
                    // the object using the entire file. The user may not realize that the object is set to not initialize,
                    // so let's ask them and offer to set Initialize to true
                    string message = $"The object {namedObject.InstanceName} has its 'Instantiate' variable set to 'false'. " +
                        $"This needs to be set to 'true' for the object to be created from the file. Set it to true?";

                    var setToTrueResponse = MessageBox.Show(message,
                        "Set Instantiate to true?",
                        MessageBoxButtons.YesNo);

                    if (setToTrueResponse == DialogResult.Yes)
                    {
                        namedObject.Instantiate = true;
                    }
                }

                GlueCommands.Self.GluxCommands.SaveGlux();
                if(element!= null)
                {
                    GlueCommands.Self.GenerateCodeCommands.GenerateElementCode(element);
                }
            }

            return true;
        }

        #endregion

        #region External File

        public void HandleDropExternalFileOnTreeNode(FilePath[] droppedFiles, ITreeNode targetNode)
        {
            ITreeNode directoryNode = null;
            ITreeNode nodeDroppedOn = targetNode;
            while (targetNode != null && !targetNode.IsEntityNode() && !targetNode.IsScreenNode())
            {
                if (directoryNode == null && targetNode.IsDirectoryNode())
                    directoryNode = targetNode;

                targetNode = targetNode.Parent;
            }

            var directoryPath = directoryNode == null ? null : directoryNode.GetRelativePath();

            bool userCancelled = false;
            if (targetNode == null)
            {
                GlueState.Self.CurrentTreeNode = targetNode;

                foreach (var fileName in droppedFiles)
                {
                    string extension = fileName.Extension;

                    if (extension == "entz" || extension == "scrz")
                    {
                        ElementImporter.ImportElementFromFile(fileName.FullPath, true);
                    }
                    else if (extension == "plug")
                    {
                        Plugins.PluginManager.InstallPlugin(InstallationType.ForUser, fileName.FullPath);
                    }
                    else
                    {
                        AddExistingFileManager.Self.AddSingleFile(fileName, ref userCancelled);
                    }
                }

                GluxCommands.Self.SaveGlux();
            }
            else if (targetNode.Tag is ScreenSave || targetNode.Tag is EntitySave)
            {
                bool any = false;
                foreach (var fileName in droppedFiles)
                {
                    // First select the entity
                    GlueState.Self.CurrentTreeNode = targetNode;

                    if (string.IsNullOrEmpty(directoryPath))
                    {
                        directoryPath = targetNode.GetRelativePath();
                    }


                    var element = GlueState.Self.CurrentElement;
                    FlatRedBall.Glue.Managers.TaskManager.Self.Add(() =>
                    {
                        var newRfs = AddExistingFileManager.Self.AddSingleFile(fileName.FullPath, ref userCancelled, element, directoryPath);

                        GlueCommands.Self.DoOnUiThread(() => GlueCommands.Self.SelectCommands.Select(newRfs));
                    },
                        "Add file " + fileName);
                    any = true;
                }
                if (any)
                {
                    GluxCommands.Self.SaveGlux();
                }
            }
        }

        #endregion

        #region General Calls

        public static async Task DragDropTreeNode(ITreeNode targetNode, ITreeNode nodeMoving)
        {
#if !DEBUG
            try
#endif
            {
                bool shouldSaveGlux = false;

                if (nodeMoving == targetNode || nodeMoving == null)
                {
                    // do nothing
                }
                else if (nodeMoving.IsEntityNode())
                {
                    await DragDropManager.Self.MoveEntityOn(nodeMoving, targetNode);
                    shouldSaveGlux = true;

                }
                else if (nodeMoving.IsReferencedFile())
                {
                    DragDropManager.Self.MoveReferencedFile(nodeMoving, targetNode);
                    shouldSaveGlux = true;
                }
                else if (nodeMoving.IsNamedObjectNode())
                {
                    DragDropManager.Self.MoveNamedObject(nodeMoving, targetNode);
                    shouldSaveGlux = true;
                }
                else if (nodeMoving.IsStateNode())
                {
                    DragDropManager.Self.MoveState(nodeMoving, targetNode);
                    shouldSaveGlux = true;
                }
                else if (nodeMoving.IsStateCategoryNode())
                {
                    DragDropManager.Self.MoveStateCategory(nodeMoving, targetNode);
                    shouldSaveGlux = true;
                }
                else if (nodeMoving.IsCustomVariable())
                {
                    MoveCustomVariable(nodeMoving, targetNode);
                    shouldSaveGlux = true;
                }



                if (shouldSaveGlux)
                {

                    var treeNodeToRefresh = targetNode.Tag is GlueElement ? targetNode : targetNode.GetContainingElementTreeNode();
                    if(treeNodeToRefresh == null)
                    {
                        treeNodeToRefresh = nodeMoving.Tag is GlueElement ? nodeMoving : nodeMoving.GetContainingElementTreeNode();
                    }
                    var elementToRefresh = treeNodeToRefresh?.Tag as GlueElement;
                    if (elementToRefresh != null)
                    {
                        GlueCommands.Self.RefreshCommands.RefreshTreeNodeFor(elementToRefresh);
                    }


                    GluxCommands.Self.SaveGlux();
                }

            }
#if !DEBUG
            catch (Exception exception)
            {
                System.Windows.Forms.MessageBox.Show("Error moving object: " + exception.ToString());
            }
#endif
        }

        #endregion
    }
}
