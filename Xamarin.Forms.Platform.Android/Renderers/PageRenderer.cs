using System;
using System.Collections.Generic;
using System.ComponentModel;
using Android.Content;
using Android.OS;
using Android.Support.V7.Widget;
using Android.Views;
using Android.Views.Accessibility;
using AView = Android.Views.View;
using Xamarin.Forms.Internals;

namespace Xamarin.Forms.Platform.Android
{
	public class PageRenderer : VisualElementRenderer<Page>, IOrderedTraversalController
	{
		public PageRenderer(Context context) : base(context)
		{
		}

		[Obsolete("This constructor is obsolete as of version 2.5. Please use PageRenderer(Context) instead.")]
		public PageRenderer()
		{
		}

		public override bool OnTouchEvent(MotionEvent e)
		{
			base.OnTouchEvent(e);

			return true;
		}

		IPageController PageController => Element as IPageController;

		IOrderedTraversalController OrderedTraversalController => this;

		double _previousHeight;

		protected override void Dispose(bool disposing)
		{
			PageController?.SendDisappearing();
			base.Dispose(disposing);
		}

		protected override void OnAttachedToWindow()
		{
			base.OnAttachedToWindow();
			var pageContainer = Parent as PageContainer;
			if (pageContainer != null && (pageContainer.IsInFragment || pageContainer.Visibility == ViewStates.Gone))
				return;
			PageController.SendAppearing();
		}

		protected override void OnDetachedFromWindow()
		{
			base.OnDetachedFromWindow();
			var pageContainer = Parent as PageContainer;
			if (pageContainer != null && pageContainer.IsInFragment)
				return;
			PageController.SendDisappearing();
		}

		protected override void OnElementChanged(ElementChangedEventArgs<Page> e)
		{
			Page view = e.NewElement;
			base.OnElementChanged(e);

			if (Id == NoId)
			{
				Id = Platform.GenerateViewId();
			}

			UpdateBackgroundColor(view);
			UpdateBackgroundImage(view);

			Clickable = true;
		}

		protected override void OnElementPropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			base.OnElementPropertyChanged(sender, e);
			if (e.PropertyName == Page.BackgroundImageProperty.PropertyName)
				UpdateBackgroundImage(Element);
			else if (e.PropertyName == VisualElement.BackgroundColorProperty.PropertyName)
				UpdateBackgroundColor(Element);
			else if (e.PropertyName == VisualElement.HeightProperty.PropertyName)
				UpdateHeight();
		}

		void UpdateHeight()
		{
			// Handle size changes because of the soft keyboard (there's probably a more elegant solution to this)

			// This is only necessary if:
			// - we're navigating back from a page where the soft keyboard was open when the user hit the Navigation Bar 'back' button
			// - the Application's content height has changed because WindowSoftInputModeAdjust was set to Resize
			// - the height has increased (in other words, the last layout was with the keyboard open, and now it's closed)
			var newHeight = Element.Height;

			if (_previousHeight > 0 && newHeight > _previousHeight)
			{
				var nav = Element.Navigation;

				// This update check will fire for all the pages on the stack, but we only need to request a layout for the top one
				if (nav?.NavigationStack != null && nav.NavigationStack.Count > 0 && Element == nav.NavigationStack[nav.NavigationStack.Count - 1])
				{
					// The Forms layout stuff is already correct, we just need to force Android to catch up
					RequestLayout();
				}
			}

			// Cache the height for next time
			_previousHeight = newHeight;
		}

		void UpdateBackgroundColor(Page view)
		{
			if (view.BackgroundColor != Color.Default)
				SetBackgroundColor(view.BackgroundColor.ToAndroid());
		}

		void UpdateBackgroundImage(Page view)
		{
			if (!string.IsNullOrEmpty(view.BackgroundImage))
				this.SetBackground(Context.GetDrawable(view.BackgroundImage));
		}

		void IOrderedTraversalController.UpdateTraversalOrder()
		{   
			// traversal order wasn't added until API 22
			if ((int)Build.VERSION.SdkInt < 22)
				return;

			// since getting and updating the traversal order is expensive, let's only do it when a screen reader is active
			// note that this does NOT get auto updated when you enable TalkBack, so the page will need to be reloaded to enable this path 
			var am = AccessibilityManager.FromContext(Context);
			if (!am.IsEnabled)
				return;

			var children = Element.Descendants();
			IDictionary<int, List<VisualElement>> tabIndexes = null;
			int childrenWithTabStopsLessOne = 0;
			AView firstTabStop = null;
			foreach (var child in children)
			{
				if (child is VisualElement ve)
				{
					if (ve.GetRenderer().View is ITabStop tabStop)
					{
						var thisControl = tabStop.TabStop;

						if (tabIndexes == null)
						{
							tabIndexes = ve.GetTabIndexesOnParentPage(out childrenWithTabStopsLessOne);
							firstTabStop = TabStopExtensions.GetFirstTabStop(tabIndexes, childrenWithTabStopsLessOne);
						}

						// this element should be the first thing focused after the root
						if (thisControl == firstTabStop)
						{
							thisControl.AccessibilityTraversalAfter = NoId;
						}
						else if (ve.IsTabStop)
						{
							AView control = ve.GetNextTabStop(forwardDirection: true,
																tabIndexes: tabIndexes,
																maxAttempts: childrenWithTabStopsLessOne);

							if (control != null && control != firstTabStop)
								control.AccessibilityTraversalAfter = thisControl.Id;
						}
					}
				}
			}
		}

		protected override void OnLayout(bool changed, int l, int t, int r, int b)
		{
			base.OnLayout(changed, l, t, r, b);
			OrderedTraversalController.UpdateTraversalOrder();
		}
	}
}