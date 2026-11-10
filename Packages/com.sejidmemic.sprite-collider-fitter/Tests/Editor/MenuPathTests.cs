using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SpriteFit.Editor;
using UnityEditor;

namespace SpriteFit.Tests
{
    /// <summary>
    /// Unity can't show one menu path as both a clickable item and a submenu; it silently hides one of them.
    /// Checks every menu item under the tool's menu root, including any declared outside the package.
    /// </summary>
    public class MenuPathTests
    {
        static List<string> ToolMenuPaths()
        {
            var paths = new List<string>();
            foreach (MethodInfo method in TypeCache.GetMethodsWithAttribute<MenuItem>())
            {
                foreach (MenuItem item in method.GetCustomAttributes<MenuItem>(false))
                {
                    if (item.validate) continue;
                    if (item.menuItem.StartsWith(SpriteFitWindow.MenuRoot.TrimEnd('/'))) paths.Add(item.menuItem);
                }
            }

            return paths;
        }

        [Test]
        public void NoMenuPathIsBothAnItemAndASubmenu()
        {
            List<string> paths = ToolMenuPaths();

            List<string> clashes = paths.Where(item => paths.Any(other => other.StartsWith(item + "/"))).ToList();

            Assert.That(clashes, Is.Empty, "These are menu items and also submenus, so Unity hides one of each pair: " + string.Join(", ", clashes));
        }

        [Test]
        public void EverythingSitsUnderOneToolsSubmenu()
        {
            List<string> paths = ToolMenuPaths();

            Assert.That(paths, Does.Contain(SpriteFitWindow.MenuRoot + "Open Window"));
            Assert.That(paths, Has.All.StartsWith(SpriteFitWindow.MenuRoot), "The menu root itself must not be an item.");
        }
    }
}
