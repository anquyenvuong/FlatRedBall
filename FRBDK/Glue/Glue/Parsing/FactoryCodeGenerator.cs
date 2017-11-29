﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FlatRedBall.Glue.CodeGeneration.CodeBuilder;
using FlatRedBall.IO;
using FlatRedBall.Glue.FormHelpers;
using FlatRedBall.Instructions.Reflection;
//using FlatRedBall.Math;
using FlatRedBall.Glue.SaveClasses;
using FlatRedBall.Glue.Elements;
using FlatRedBall.Glue.CodeGeneration;
using FlatRedBall.Glue.IO;
using FlatRedBall.Glue.Plugins.ExportedImplementations;
using FlatRedBall.Glue.Plugins.Interfaces;

namespace FlatRedBall.Glue.Parsing
{
    public class FactoryCodeGeneratorEarly : ElementComponentCodeGenerator
    {
        public override CodeLocation CodeLocation
        {
            get
            {
                return CodeLocation.BeforeStandardGenerated;
            }
        }

        public override ICodeBlock GenerateDestroy(ICodeBlock codeBlock, IElement element)
        {
            // This needs to be before the base.Destroy(); call so that the derived class can make itself unused before the base class get a chance
            if (element is EntitySave && (element as EntitySave).CreatedByOtherEntities)
            {
                codeBlock
                    .If("Used")
                        .Line(string.Format("Factories.{0}Factory.MakeUnused(this, false);", FileManager.RemovePath(element.Name)));
            }

            return codeBlock;
        }

    }


    public class FactoryCodeGenerator : ElementComponentCodeGenerator
    {
        static FactoryEntireClassGenerator mEntireClassGenerator = new FactoryEntireClassGenerator();

        #region CodeGenerator methods (for generating code in an Element)

        public override ICodeBlock GenerateFields(ICodeBlock codeBlock, IElement element)
        {
            return codeBlock;
        }

        public override ICodeBlock GenerateInitialize(ICodeBlock codeBlock, IElement element)
        {
            return codeBlock;
        }

        public override ICodeBlock GenerateAddToManagers(ICodeBlock codeBlock, IElement element)
        {
            // September 9, 2011
            // I think factories should
            // only initialize on a Screen
            // and not Entity.  Otherwise Entities
            // that contain lists of Factoried objects
            // will initialize the list, and this could
            // cause the list to become the one that the
            // factory fills.
            /////////////////////EARLY OUT///////////////////////
            if (!(element is ScreenSave))
            {
                return codeBlock;
            }
            /////////////////END EARLY OUT//////////////////////

            // June 5, 2011
            // We used to instantiate
            // Factories in the Initialize
            // method, but that caused a bug.
            // If a factory is used in 2 consecutive
            // Screens, and if the first screen loads
            // the next screen asynchronously, and if the
            // two Screens use different ContentManagers, then
            // the Destroy call on the first screen will wipe out
            // the Initialize call that was asynchronously called from
            // the second Screen.  We fix this by moving the Initialize
            // method into AddToManagers so that it's not called asynchronously - 
            // instead it's called after the first screen has finished unloading itself.

            var entityLists = element.NamedObjects
                .Where(nos => !nos.InstantiatedByBase &&
                    nos.SourceType == SourceType.FlatRedBallType &&
                    nos.IsList &&
                    nos.IsDisabled == false &&
                    !string.IsNullOrEmpty(nos.SourceClassGenericType) &&
                    (nos.SourceClassGenericType.StartsWith("Entities\\") || nos.SourceClassGenericType.StartsWith("Entities/")))
                .ToList();


            List<EntitySave> entitiesToInitializeFactoriesFor = new List<EntitySave>();
            foreach(var listNos in entityLists)
            {
                EntitySave sourceEntitySave = ObjectFinder.Self.GetEntitySave(listNos.SourceClassGenericType);

                if(sourceEntitySave != null)
                {
                    if(sourceEntitySave.CreatedByOtherEntities && !entitiesToInitializeFactoriesFor.Contains(sourceEntitySave))
                    {
                        entitiesToInitializeFactoriesFor.Add(sourceEntitySave);
                    }
                }
            }

            foreach(var entity in entitiesToInitializeFactoriesFor)
            {
                // initialize the factory:
                string entityClassName = FileManager.RemovePath(entity.Name);
                string factoryName = entityClassName + "Factory";
                codeBlock.Line(factoryName + ".Initialize(ContentManagerName);");

                // find any lists of entities that are of this type, or of a derived type.
                foreach(var listNos in entityLists)
                {
                    EntitySave listEntitySave = ObjectFinder.Self.GetEntitySave(listNos.SourceClassGenericType);

                    if(listEntitySave != null && (listEntitySave == entity || entity.InheritsFrom(listEntitySave.Name)))
                    {
                        codeBlock.Line($"{factoryName}.AddList({listNos.FieldName});");
                    }
                }
            }

            return codeBlock;
        }

        public override ICodeBlock GenerateDestroy(ICodeBlock codeBlock, IElement element)
        {
            foreach (NamedObjectSave namedObject in element.NamedObjects)
            {

                if (namedObject.SourceClassType == "PositionedObjectList<T>" && !string.IsNullOrEmpty(namedObject.SourceClassGenericType) &&
                    namedObject.SourceClassGenericType.StartsWith("Entities\\"))
                {
                    EntitySave sourceEntitySave = ObjectFinder.Self.GetEntitySave(namedObject.SourceClassGenericType);

                    if (sourceEntitySave == null)
                    {
                        Plugins.PluginManager.ReceiveError("Could not find the Entity " + namedObject.SourceClassGenericType + " which is referenced by " +
                            namedObject);
                        return codeBlock;
                    }

                    // August 23, 2012
                    // We don't Initialize
                    // factories inside of Entities,
                    // but for some reason we still destroyed
                    // them.  Added the check for element is ScreenSave.
                    if(element is ScreenSave)
                    {
                        if (sourceEntitySave.CreatedByOtherEntities)
                        {
                            string entityClassName = FileManager.RemovePath(namedObject.SourceClassGenericType);
                            string line = entityClassName + "Factory.Destroy();";

                            codeBlock.Line(line);
                        }

                        if (string.IsNullOrEmpty(sourceEntitySave.BaseEntity))
                        {
                            List<EntitySave> derivedList = ObjectFinder.Self.GetAllEntitiesThatInheritFrom(sourceEntitySave);

                            foreach (EntitySave derivedEntity in derivedList)
                            {
                                if (derivedEntity.CreatedByOtherEntities)
                                {
                                    string derivedName = FileManager.RemovePath(derivedEntity.Name);
                                    string replaceLine = derivedName + "Factory.Destroy();";

                                    codeBlock.Line(replaceLine);
                                }
                            }
                        }
                    }

                    
                }

            }

            return codeBlock;
        }

        public override ICodeBlock GenerateActivity(ICodeBlock codeBlock, IElement element)
        {
            return codeBlock;
        }

        public override ICodeBlock GenerateAdditionalMethods(ICodeBlock codeBlock, IElement element)
        {
            return codeBlock;
        }

        public override ICodeBlock GenerateLoadStaticContent(ICodeBlock codeBlock, IElement element)
        {
            return codeBlock;
            // TODO:  We should load static content for factories here
        }
        #endregion

        public static void CallPostInitializeIfNecessary(IElement element, ICodeBlock currentBlock)
        {
            bool isEntity = element is EntitySave;

            bool shouldCallPostInitializeBecauseIsPooled = false;

            // It may inherit from a FRB type, in which case we still want to add PostInitialize
            //if (isEntity && string.IsNullOrEmpty(element.BaseElement))
            if (isEntity && !element.InheritsFromElement())
            {


                EntitySave asEntitySave = element as EntitySave;

                shouldCallPostInitializeBecauseIsPooled =
                    asEntitySave.CreatedByOtherEntities && asEntitySave.PooledByFactory;

                if (!shouldCallPostInitializeBecauseIsPooled)
                {
                    List<EntitySave> entities =
                        ObjectFinder.Self.GetAllEntitiesThatInheritFrom(asEntitySave);

                    foreach (EntitySave derivedEntity in entities)
                    {
                        if (derivedEntity.CreatedByOtherEntities && derivedEntity.PooledByFactory)
                        {
                            shouldCallPostInitializeBecauseIsPooled = true;
                            break;
                        }
                    }
                }

            }


            if (shouldCallPostInitializeBecauseIsPooled)
            {
                currentBlock.Line("PostInitialize();");
            }

        }

        public static void RemoveFactory(EntitySave entitySave)
        {
            mEntireClassGenerator.EntitySave = entitySave;

            try
            {
                // Delete the file and remove it from the project
                if (System.IO.File.Exists(mEntireClassGenerator.ProjectSpecificFullFileName))
                {
                    FileHelper.DeleteFile(mEntireClassGenerator.ProjectSpecificFullFileName);
                }
                mEntireClassGenerator.RemoveSelfFromProject();
            }
            catch
            {
                System.Windows.Forms.MessageBox.Show("Error trying to remove the Factory " + mEntireClassGenerator.ClassName);

            }
        }

        public static void UpdateFactoryClass(EntitySave entitySave)
        {
            mEntireClassGenerator.EntitySave = entitySave;

            mEntireClassGenerator.GenerateAndAddToProjectIfNecessary();
        }

        public static void AddGeneratedPerformanceTypes()
        {
            string poolListFileName = FileManager.RelativeDirectory + @"Performance\PoolList.Generated.cs";
            string iEntityFactoryFileName = FileManager.RelativeDirectory + @"Performance\IEntityFactory.Generated.cs";

            // April 28, 2015
            // We killed IPoolable
            // from generated code as
            // it moved into the engine.
            // If anyone has old code we want
            // to re-generate it to overwrite the
            // file so the generated code uses the new
            // namespace. Eventually we should bring this
            // if-check back in, but for now, we'll leave it out:
            //if (!FileManager.FileExists(poolListFileName) || !FileManager.FileExists(iEntityFactoryFileName))
            {
                // Vic says:  This could be optimized, but it might not be worth the extra complexity
                // since this method is likely really fast.
                string contents = Resources.Resource1.PoolList;
                contents = CodeWriter.ReplaceNamespace(contents, ProjectManager.ProjectNamespace + ".Performance");
                FileManager.SaveText(contents, poolListFileName);

                contents = Resources.Resource1.IEntityFactory;
                contents = CodeWriter.ReplaceNamespace(contents, ProjectManager.ProjectNamespace + ".Performance");
                FileManager.SaveText(contents, iEntityFactoryFileName);

            }

            // These files may exist, but not be part of the project, so let's make sure that they are
            // part of the project
            bool wasPoolListAdded = GlueCommands.Self.ProjectCommands.UpdateFileMembershipInProject(ProjectManager.ProjectBase, poolListFileName, false, false);
            bool wasEntityFactoryAdded = GlueCommands.Self.ProjectCommands.UpdateFileMembershipInProject(ProjectManager.ProjectBase, iEntityFactoryFileName, false, false);
            if (wasPoolListAdded || wasEntityFactoryAdded)
            {
                Managers.TaskManager.Self.AddAsyncTask( ProjectManager.SaveProjects, "Saving Project because of performance file adds");
            }

        }

    }

    public class FactoryEntireClassGenerator : EntireClassCodeGenerator
    {
        public bool ShouldPoolObjects
        {
            get{ return EntitySave.PooledByFactory;}
        }


        public EntitySave EntitySave
        {
            get;
            set;
        }

        public override string ClassName
        {
            get { return FileManager.RemovePath(EntitySave.Name) + "Factory"; }
        }

        public override string Namespace
        {
            get { return "Factories"; }
        }

        public override string GetCode()
        {

            string entityClassName = FileManager.RemovePath(FileManager.RemoveExtension(EntitySave.Name));
            
            string baseEntityName = null;
            if (!string.IsNullOrEmpty(EntitySave.BaseEntity))
            {
                EntitySave rootEntitySave = EntitySave.GetRootBaseEntitySave();

                // There could be an invalid inheritance chain.  We don't want Glue to bomb if so, so
                // we'll check for this.
                if (rootEntitySave != null && rootEntitySave != EntitySave)
                {
                    baseEntityName = rootEntitySave.Name;
                }
            }


            string factoryClassName = ClassName;

            ClassProperties classProperties = new ClassProperties();

            classProperties.NamespaceName = ProjectManager.ProjectNamespace + ".Factories";
            classProperties.ClassName = factoryClassName + " : IEntityFactory";
            
            classProperties.Members = new List<FlatRedBall.Instructions.Reflection.TypedMemberBase>();

            classProperties.UntypedMembers = new Dictionary<string, string>();
            string positionedObjectListType = string.Format("FlatRedBall.Math.PositionedObjectList<{0}>", entityClassName);
            
            // Factories used to be always static but we're going to make them singletons instead
            //classProperties.IsStatic = true;

            classProperties.UsingStatements = new List<string>();
            classProperties.UsingStatements.Add(GlueCommands.Self.GenerateCodeCommands.GetNamespaceForElement(EntitySave));
            classProperties.UsingStatements.Add("System");

            if (!string.IsNullOrEmpty(baseEntityName))
            {
                EntitySave baseEntity = ObjectFinder.Self.GetEntitySave(baseEntityName);
                classProperties.UsingStatements.Add(GlueCommands.Self.GenerateCodeCommands.GetNamespaceForElement(baseEntity));
            }


            classProperties.UsingStatements.Add("FlatRedBall.Math");
            classProperties.UsingStatements.Add("FlatRedBall.Graphics");
            classProperties.UsingStatements.Add(ProjectManager.ProjectNamespace + ".Performance");


            ICodeBlock codeContent = CodeWriter.CreateClass(classProperties);

            const int numberOfInstancesToPool = 20;

            var methodTag = codeContent.GetTag("Methods")[0];

            var methodBlock = GetAllFactoryMethods(factoryClassName, baseEntityName, numberOfInstancesToPool,
                                                ShouldPoolObjects);
            methodTag.InsertBlock(methodBlock);

            var codeBlock = new CodeBlockBase(null);
            codeBlock.Line("static string mContentManagerName;");
            codeBlock.Line("static System.Collections.Generic.List<System.Collections.IList> listsToAddTo = new System.Collections.Generic.List<System.Collections.IList>();");

            codeBlock.Line(string.Format("static PoolList<{0}> mPool = new PoolList<{0}>();", entityClassName));

            codeBlock.Line(string.Format("public static Action<{0}> EntitySpawned;", entityClassName));

            codeBlock.Function("object", "IEntityFactory.CreateNew", "")
                .Line(string.Format("return {0}.CreateNew();", factoryClassName));

            codeBlock.Function("object", "IEntityFactory.CreateNew", "Layer layer")
                .Line(string.Format("return {0}.CreateNew(layer);", factoryClassName));

            #region Self and mSelf
            codeBlock.Line("static " + factoryClassName + " mSelf;");

            var selfProperty = codeBlock.Property("public static " + factoryClassName, "Self");
            var selfGet = selfProperty.Get();
            selfGet.If("mSelf == null")
                .Line("mSelf = new " + entityClassName + "Factory" + "();");
            selfGet.Line("return mSelf;");
            #endregion

            ((codeContent.BodyCodeLines.Last() as CodeBlockBase).BodyCodeLines.Last() as CodeBlockBase).InsertBlock(codeBlock);
            return codeContent.ToString();
        }

        private static ICodeBlock GetAllFactoryMethods(string factoryClassName, string baseClassName, int numberToPreAllocate, bool poolObjects)
        {
            string className = factoryClassName.Substring(0, factoryClassName.Length - "Factory".Length);

            ICodeBlock codeBlock = new CodeDocument();

            codeBlock.Line("public static FlatRedBall.Math.Axis? SortAxis { get; set;}");

            GetCreateNewFactoryMethod(codeBlock, factoryClassName, poolObjects, baseClassName);
            codeBlock._();
            GetInitializeFactoryMethod(codeBlock, className, poolObjects, "mScreenListReference");
            codeBlock._();

            GetDestroyFactoryMethod(codeBlock, factoryClassName);
            codeBlock._();
            GetFactoryInitializeMethod(codeBlock, factoryClassName, numberToPreAllocate);
            codeBlock._();
            GetMakeUnusedFactory(codeBlock, factoryClassName, poolObjects);
            codeBlock._();


            string whereClass = className;
            if(!string.IsNullOrEmpty(baseClassName))
            {
                whereClass = baseClassName.Replace("\\", ".");
            }
            AddAddListMethod(codeBlock, whereClass);

            return codeBlock;
        }

        private static void AddAddListMethod(ICodeBlock codeBlock, string entityClassName)
        {
            var method = codeBlock.Function("public static void", "AddList<T>", "System.Collections.Generic.IList<T> newList", $"where T : {entityClassName}");
            method.Line("listsToAddTo.Add(newList as System.Collections.IList);");
        }

        private static ICodeBlock GetCreateNewFactoryMethod(ICodeBlock codeBlock, string className, bool poolObjects, string baseEntityName)
        {
            className = className.Substring(0, className.Length - "Factory".Length);

            // no tabs needed on first line
            codeBlock
                .Function(StringHelper.SpaceStrings("public", "static", className), "CreateNew", "float x = 0, float y = 0")
                    .Line("return CreateNew(null, x, y);")
                .End();

            codeBlock = codeBlock
                .Function(StringHelper.SpaceStrings("public", "static", className), "CreateNew", "Layer layer, float x = 0, float y = 0")
                    .If("string.IsNullOrEmpty(mContentManagerName)")
                        .Line("throw new System.Exception(\"You must first initialize the factory to use it. You can either add PositionedObjectList of type " +
                            className + " (the most common solution) or call Initialize in custom code\");")
                    .End()

                    .Line(className + " instance = null;");

            if (poolObjects)
            {
                codeBlock
                    .Line("instance = mPool.GetNextAvailable();")
                    .If("instance == null")
                        .Line("mPool.AddToPool(new " + className + "(mContentManagerName, false));")
                        .Line("instance =  mPool.GetNextAvailable();")
                    .End()
                    .Line("instance.AddToManagers(layer);");
            }
            else
            {
                codeBlock
                    .Line(string.Format("instance = new {0}(mContentManagerName, false);", className))
                    .Line("instance.AddToManagers(layer);");
            }

            codeBlock.Line("instance.X = x;");
            codeBlock.Line("instance.Y = y;");

            CreateAddToListCode(codeBlock, className);

            codeBlock = codeBlock
                .If("EntitySpawned != null")
                    .Line("EntitySpawned(instance);")
                .End()
                .Line("return instance;")
            .End();

            return codeBlock;
        }

        private static ICodeBlock CreateAddToListCode(ICodeBlock codeBlock, string className)
        {

            codeBlock
                .ForEach("var list in listsToAddTo")
                    .If($"SortAxis == FlatRedBall.Math.Axis.X && list is PositionedObjectList<{className}>")
                        .Line($"var index = (list as PositionedObjectList<{className}>).GetFirstAfter(x, Axis.X, 0, list.Count);")
                        .Line($"list.Insert(index, instance);")
                    .End().ElseIf($"SortAxis == FlatRedBall.Math.Axis.Y && list is PositionedObjectList<{className}>")
                        .Line($"var index = (list as PositionedObjectList<{className}>).GetFirstAfter(y, Axis.Y, 0, list.Count);")
                        .Line($"list.Insert(index, instance);")
                    .End().Else()
                        .Line("// Sort Z not supported")
                        .Line("list.Add(instance);")
                    .End()
                .End();

            return codeBlock;
        }

        private static ICodeBlock GetDestroyFactoryMethod(ICodeBlock codeBlock, string className)
        {
            className = className.Substring(0, className.Length - "Factory".Length);

            codeBlock
                .Function("public static void", "Destroy", "")
                    .Line("mContentManagerName = null;")
                    .Line("listsToAddTo.Clear();")
                    .Line("SortAxis = null;")
                    .Line("mPool.Clear();")
                    .Line("EntitySpawned = null;")
                .End();

            return codeBlock;
        }

        private static ICodeBlock GetFactoryInitializeMethod(ICodeBlock codeBlock, string factoryClassName, int numberToPreAllocate)
        {
            string entityClassName = factoryClassName.Substring(0, factoryClassName.Length - "Factory".Length);

            codeBlock
                .Function("private static void", "FactoryInitialize", "")
                    .Line("const int numberToPreAllocate = " + numberToPreAllocate + ";")
                    .For("int i = 0; i < numberToPreAllocate; i++")
                        .Line(string.Format("{0} instance = new {0}(mContentManagerName, false);", entityClassName))
                        .Line("mPool.AddToPool(instance);")
                    .End()
                .End();

            return codeBlock;
        }

        private static ICodeBlock GetInitializeFactoryMethod(ICodeBlock codeBlock, string className, bool poolObjects, string listToAssign)
        {
            codeBlock = codeBlock
                .Function("public static void", "Initialize", string.Format("string contentManager", className))
                    .Line("mContentManagerName = contentManager;");

            if (poolObjects)
            {
                codeBlock.Line("FactoryInitialize();");
            }

            codeBlock = codeBlock.End();

            return codeBlock;
        }

        private static ICodeBlock GetMakeUnusedFactory(ICodeBlock codeBlock, string factoryClassName, bool poolObjects)
        {
            string className = factoryClassName.Substring(0, factoryClassName.Length - "Factory".Length);

            codeBlock.Line("/// <summary>");
            codeBlock.Line("/// Makes the argument objectToMakeUnused marked as unused.  This method is generated to be used");
            codeBlock.Line("/// by generated code.  Use Destroy instead when writing custom code so that your code will behave");
            codeBlock.Line("/// the same whether your Entity is pooled or not.");
            codeBlock.Line("/// </summary>");

            codeBlock = codeBlock
                .Function("public static void", "MakeUnused", className + " objectToMakeUnused")
                    .Line("MakeUnused(objectToMakeUnused, true);")
                .End()
                ._()
                .Line("/// <summary>")
                .Line("/// Makes the argument objectToMakeUnused marked as unused.  This method is generated to be used")
                .Line("/// by generated code.  Use Destroy instead when writing custom code so that your code will behave")
                .Line("/// the same whether your Entity is pooled or not.")
                .Line("/// </summary>")
                .Function("public static void", "MakeUnused", className + " objectToMakeUnused, bool callDestroy");
                    
            if (poolObjects)
            {
                codeBlock
                    .Line("mPool.MakeUnused(objectToMakeUnused);")
                    ._()
                    .If("callDestroy")
                        .Line("objectToMakeUnused.Destroy();")
                    .End();
            }
            else
            {
                // We still need to check if we should call destroy even if not pooled, because the parent may be pooled, in which case an infinite loop
                // can occur if we don't check the callDestroy value. More info on this bug:
                // http://www.hostedredmine.com/issues/413966
                codeBlock
                    .If("callDestroy")
                        .Line("objectToMakeUnused.Destroy();");
            }

            codeBlock = codeBlock.End();

            return codeBlock;
        }


    }



}
