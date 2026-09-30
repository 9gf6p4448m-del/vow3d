#if UNITY_EDITOR
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;

namespace Vow.Tests.PlayMode
{
    // PlayMode 的 Screen 必須來自真實 render size；視窗外框尺寸與 Screen.SetResolution 不足以保證它。
    [SetUpFixture]
    public sealed class EditorGameViewSetUpFixture
    {
        private EditorWindow _gameView;
        private object _sizeGroup;
        private MethodInfo _selectSize;
        private int _originalSizeIndex;
        private int _temporarySizeIndex = -1;

        [OneTimeSetUp]
        public void SetUpGameView()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static;
            Assembly assembly = typeof(EditorWindow).Assembly;
            Type viewType = assembly.GetType("UnityEditor.GameView", true);
            _gameView = EditorWindow.GetWindow(viewType);
            _originalSizeIndex = (int)viewType.GetProperty("selectedSizeIndex", flags).GetValue(_gameView);
            _selectSize = viewType.GetMethod("SizeSelectionCallback", flags);
            Type sizesType = assembly.GetType("UnityEditor.GameViewSizes", true);
            object sizes = sizesType.BaseType.GetProperty("instance", flags).GetValue(null);
            object groupType = viewType.GetProperty("currentSizeGroupType", flags).GetValue(null);
            _sizeGroup = sizesType.GetMethod("GetGroup", flags).Invoke(sizes, new[] { groupType });
            Type sizeType = assembly.GetType("UnityEditor.GameViewSize", true);
            Type kindType = assembly.GetType("UnityEditor.GameViewSizeType", true);
            object fixedResolution = Enum.Parse(kindType, "FixedResolution");
            object size = Activator.CreateInstance(sizeType,
                new object[] { fixedResolution, 640, 480, "PlayMode temporary 640x480" });
            int index = (int)_sizeGroup.GetType().GetMethod("GetTotalCount").Invoke(_sizeGroup, null);
            _sizeGroup.GetType().GetMethod("AddCustomSize").Invoke(_sizeGroup, new[] { size });
            _temporarySizeIndex = index;
            try
            {
                // callback 同步 PlayModeView.targetSize，單設 selectedSizeIndex 不會更新實際 render size。
                _selectSize.Invoke(_gameView, new object[] { index, null });
                _gameView.Repaint();
            }
            catch
            {
                RestoreGameView();
                throw;
            }
        }

        [OneTimeTearDown]
        public void RestoreGameView()
        {
            if (_temporarySizeIndex < 0) return;
            try
            {
                _selectSize.Invoke(_gameView, new object[] { _originalSizeIndex, null });
                _gameView.Repaint();
            }
            finally
            {
                _sizeGroup.GetType().GetMethod("RemoveCustomSize")
                    .Invoke(_sizeGroup, new object[] { _temporarySizeIndex });
                _temporarySizeIndex = -1;
            }
        }
    }
}
#endif
