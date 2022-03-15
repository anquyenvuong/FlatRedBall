﻿using FlatRedBall.Content.AnimationChain;
using FlatRedBall.Glue.Elements;
using FlatRedBall.Glue.Plugins.ExportedImplementations;
using FlatRedBall.Glue.SaveClasses;
using FlatRedBall.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OfficialPlugins.PreviewGenerator.Managers
{
    public static class PreviewGenerationLogic
    {
        public static ImageSource GetImageSourceForSelection(NamedObjectSave namedObjectSave, GlueElement element, StateSave state)
        {

            List<NamedObjectSave> visibleNamedObjects = null;

            if (namedObjectSave != null)
            {
                visibleNamedObjects = new List<NamedObjectSave>() { namedObjectSave };
            }
            else
            {
                visibleNamedObjects = element.AllNamedObjects.Where(item =>
                {
                    var visibleAsString = GetEffectiveValue(state, item, "Visible", element);
                    return visibleAsString?.ToLowerInvariant() != "false" &&
                        (item.GetAssetTypeInfo() == AvailableAssetTypes.CommonAtis.Sprite ||
                         item.GetAssetTypeInfo() == AvailableAssetTypes.CommonAtis.Circle);
                }).ToList();
            }

            ImageSource imageSource = null;
            foreach (var nos in visibleNamedObjects)
            {
                imageSource = GetImageSourceForNamedObject(nos, element, state);
                if(imageSource != null)
                {
                    break;
                }
            }

            return imageSource;
        }

        private static ImageSource GetImageSourceForNamedObject(NamedObjectSave namedObjectSave, GlueElement element, StateSave state)
        {
            string textureName, achxName, chainName;
            GetVariablesForCreatingPreview(namedObjectSave, element, state, out textureName, out achxName, out chainName);

            FilePath textureFilePath;
            int? left, top, width, height;
            textureName = GetCoordinates(element, textureName, achxName, chainName, out textureFilePath, out left, out top, out width, out height);

            if (textureFilePath == null && !string.IsNullOrEmpty(textureName))
            {
                var rfs = element.GetReferencedFileSaveRecursively(textureName);

                if (rfs != null)
                {
                    textureFilePath = GlueCommands.Self.GetAbsoluteFilePath(rfs);
                }
            }

            ImageSource imageSource = null;

            if (textureFilePath?.Exists() == true)
            {
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.UriSource = new Uri(textureFilePath.FullPath, UriKind.Relative);
                bitmapImage.EndInit();

                imageSource = bitmapImage;

                if (left != null)
                {
                    var croppedBitmap = new CroppedBitmap();
                    croppedBitmap.BeginInit();
                    croppedBitmap.SourceRect = new Int32Rect(left.Value, top.Value, width.Value, height.Value);
                    croppedBitmap.Source = bitmapImage;
                    croppedBitmap.EndInit();

                    imageSource = croppedBitmap;
                }

            }

            if (imageSource == null && namedObjectSave != null)
            {
                var red = GetEffectiveValue(state, namedObjectSave, "Red", element) ?? "0";
                var green = GetEffectiveValue(state, namedObjectSave, "Green", element) ?? "0";
                var blue = GetEffectiveValue(state, namedObjectSave, "Blue", element) ?? "0";
                if (namedObjectSave?.GetAssetTypeInfo() == AvailableAssetTypes.CommonAtis.Sprite)
                {
                    // is it solid color?
                    var colorOperation = GetEffectiveValue(state, namedObjectSave, "ColorOperation", element);

                    if (colorOperation == "Color" || colorOperation == ((int)(FlatRedBall.Graphics.ColorOperation.Color)).ToString())
                    {
                        float.TryParse(red, out float redValue);
                        float.TryParse(green, out float greenValue);
                        float.TryParse(blue, out float blueValue);

                        var solidBrush = new SolidColorBrush(Color.FromRgb(
                            (byte)(255 * redValue),
                            (byte)(255 * greenValue),
                            (byte)(255 * blueValue)));

                        DrawingBrush myDrawingBrush = new DrawingBrush();

                        // Create a drawing.
                        GeometryDrawing myGeometryDrawing = new GeometryDrawing();
                        myGeometryDrawing.Brush = solidBrush;
                        //myGeometryDrawing.Pen = new Pen(Brushes.Tran, 1);
                        GeometryGroup geometryGroup = new GeometryGroup();
                        geometryGroup.Children.Add(new RectangleGeometry(new Rect(0, 0, 64, 64)));
                        myGeometryDrawing.Geometry = geometryGroup;
                        myDrawingBrush.Drawing = myGeometryDrawing;

                        imageSource = BitmapSourceFromBrush(myDrawingBrush, 64);
                    }
                }
                else if (namedObjectSave?.GetAssetTypeInfo() == AvailableAssetTypes.CommonAtis.Circle)
                {
                    SolidColorBrush solidBrush = GetSolidColorBrushForShape(namedObjectSave, element, state);

                    DrawingBrush myDrawingBrush = new DrawingBrush();

                    // Create a drawing.
                    GeometryDrawing myGeometryDrawing = new GeometryDrawing();
                    //myGeometryDrawing.Brush = solidBrush;
                    myGeometryDrawing.Pen = new Pen(solidBrush, 2);
                    GeometryGroup geometryGroup = new GeometryGroup();
                    geometryGroup.Children.Add(new EllipseGeometry(new Rect(1, 1, 62, 62)));
                    myGeometryDrawing.Geometry = geometryGroup;
                    myDrawingBrush.Drawing = myGeometryDrawing;

                    imageSource = BitmapSourceFromBrush(myDrawingBrush, 64);
                }
                else if(namedObjectSave?.GetAssetTypeInfo() == AvailableAssetTypes.CommonAtis.AxisAlignedRectangle)
                {
                    SolidColorBrush solidBrush = GetSolidColorBrushForShape(namedObjectSave, element, state);
                    DrawingBrush myDrawingBrush = new DrawingBrush();

                    // Create a drawing.
                    GeometryDrawing myGeometryDrawing = new GeometryDrawing();
                    //myGeometryDrawing.Brush = solidBrush;
                    myGeometryDrawing.Pen = new Pen(solidBrush, 2);
                    GeometryGroup geometryGroup = new GeometryGroup();
                    geometryGroup.Children.Add(new RectangleGeometry(new Rect(1, 1, 62, 62)));
                    myGeometryDrawing.Geometry = geometryGroup;
                    myDrawingBrush.Drawing = myGeometryDrawing;

                    imageSource = BitmapSourceFromBrush(myDrawingBrush, 64);
                }
            }

            return imageSource;
        }

        private static SolidColorBrush GetSolidColorBrushForShape(NamedObjectSave namedObjectSave, GlueElement element, StateSave state)
        {
            var color = GetEffectiveValue(state, namedObjectSave, "Color", element) ?? "White";

            var matchingColor = typeof(Microsoft.Xna.Framework.Color).GetProperty(color);
            var colorValue = (Microsoft.Xna.Framework.Color)matchingColor.GetValue(null);


            var solidBrush = new SolidColorBrush(Color.FromRgb(
                colorValue.R,
                colorValue.G,
                colorValue.B));
            return solidBrush;
        }

        public static BitmapSource BitmapSourceFromBrush(Brush drawingBrush, int size = 32, int dpi = 96)
        {
            // RenderTargetBitmap = builds a bitmap rendering of a visual
            var pixelFormat = PixelFormats.Pbgra32;
            RenderTargetBitmap rtb = new RenderTargetBitmap(size, size, dpi, dpi, pixelFormat);

            // Drawing visual allows us to compose graphic drawing parts into a visual to render
            var drawingVisual = new DrawingVisual();
            using (DrawingContext context = drawingVisual.RenderOpen())
            {
                // Declaring drawing a rectangle using the input brush to fill up the visual
                context.DrawRectangle(drawingBrush, null, new Rect(0, 0, size, size));
            }

            // Actually rendering the bitmap
            rtb.Render(drawingVisual);
            return rtb;
        }

        private static void GetVariablesForCreatingPreview(NamedObjectSave namedObjectSave, GlueElement element, StateSave state, out string textureName, out string achxName, out string chainName)
        {
            textureName = null;
            achxName = null;
            chainName = null;
            if (namedObjectSave?.GetAssetTypeInfo() == AvailableAssetTypes.CommonAtis.Sprite)
            {
                textureName = GetEffectiveValue(state, namedObjectSave, "Texture", element);
                achxName = GetEffectiveValue(state, namedObjectSave, "AnimationChains", element);
                chainName = GetEffectiveValue(state, namedObjectSave, "CurrentChainName", element);
            }
        }

        private static string GetEffectiveValue(StateSave state, NamedObjectSave namedObjectSave, string variableName, GlueElement owner)
        {
            var variable = owner.CustomVariables.FirstOrDefault(item => item.SourceObject == namedObjectSave.InstanceName && item.SourceObjectProperty == variableName);

            string valueToReturn = null;

            if (variable != null)
            {
                var matchingInstruction = state?.InstructionSaves.FirstOrDefault(item => item.Member == variable.Name);

                valueToReturn = (matchingInstruction?.Value as string) ?? matchingInstruction?.Value?.ToString();
            }

            if(string.IsNullOrEmpty(valueToReturn))
            {
                var customVariableInNamedObject = namedObjectSave.GetCustomVariable(variableName);
                valueToReturn = (customVariableInNamedObject?.Value as string) ?? customVariableInNamedObject?.Value.ToString();
            }

            return valueToReturn;
        }

        private static string GetCoordinates(GlueElement element, string textureName, string achxName, string chainName, out FilePath textureFilePath, out int? left, out int? top, out int? width, out int? height)
        {
            textureFilePath = null;
            left = null;
            top = null;
            width = null;
            height = null;
            if (!string.IsNullOrEmpty(achxName))
            {
                var rfs = element.GetReferencedFileSaveRecursively(achxName);
                var achxFullPath = GlueCommands.Self.GetAbsoluteFilePath(rfs);

                if (achxFullPath.Exists())
                {
                    var animationChainListSave = AnimationChainListSave.FromFile(achxFullPath.FullPath);

                    AnimationChainSave animationChainSave = null;
                    animationChainSave = animationChainListSave.AnimationChains.FirstOrDefault(item => item.Name == chainName);
                    if (animationChainSave == null)
                    {
                        animationChainSave = animationChainListSave.AnimationChains.FirstOrDefault();
                    }

                    var frame = animationChainSave.Frames.FirstOrDefault();
                    left = (int)frame?.LeftCoordinate;
                    width = (int)frame?.RightCoordinate - left;
                    top = (int)frame?.TopCoordinate;
                    height = (int)frame?.BottomCoordinate - top;

                    textureName = frame.TextureName;

                    textureFilePath = achxFullPath.GetDirectoryContainingThis() + textureName;

                }
            }

            return textureName;
        }

    }
}