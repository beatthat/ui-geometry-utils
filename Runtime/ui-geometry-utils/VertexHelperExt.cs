using BeatThat.Pools;
using BeatThat.Rects;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

namespace BeatThat.UIGeometryUtils
{
	public static class VertextHelperUtils 
	{
		/// <summary>
		/// Add verts and tris for a rect with clipping applied.
		/// IF AND ONLY IF the given rect is at least partially inside the clip rect.
		/// </summary>
		/// <returns><c>TRUE</c>, if the a rect was added (given rect intersects clip rect), <c>FALSE</c> otherwise.</returns>
		/// <param name="vh">The VertexHelper</param>
		/// <param name="r">The rect to add </param>
		/// <param name="c">Vertex color</param>
		/// <param name="clipRect">The clip rect.</param>
		/// <param name="vertOff">The vertex offset for creating tris.</param>
		public static int AddRectClipped(this VertexHelper vh, Rect r, Color32 c, Rect clipRect, int vertOff)
		{
			Rect rClipped;
			if(!clipRect.Intersects(r, out rClipped)) {
				return 0;
			}
			var uvRect = new Rect(0f, 0f, 1f, 1f);
			var wClip = r.width - rClipped.width;
			if(wClip > 0f) {
				var uWidth = wClip / r.width;

				uvRect.xMin = uWidth * (Mathf.Abs(r.xMin - rClipped.xMin) / wClip);
				uvRect.xMax = 1f - (uWidth * ((Mathf.Abs(rClipped.xMax - r.xMax)) / wClip));
			}
			var hClip = r.height - rClipped.height;
			if(hClip > 0f) {
				var uHeight = hClip / r.height;
				uvRect.yMin = uHeight * Mathf.Abs(rClipped.yMin - r.yMin) / hClip;
				uvRect.yMax = 1f - (uHeight * (Mathf.Abs(rClipped.yMax - r.yMax) / hClip));
			}
			if(debugging) {
				c = NextDebugColor();
			}
			vh.AddVert(new Vector3(rClipped.xMin, rClipped.yMin), c, uvRect.min);
			vh.AddVert(new Vector3(rClipped.xMin, rClipped.yMax), c, new Vector2(uvRect.xMin, uvRect.yMax));
			vh.AddVert(new Vector3(rClipped.xMax, rClipped.yMax), c, uvRect.max);
			vh.AddVert(new Vector3(rClipped.xMax, rClipped.yMin), c, new Vector2(uvRect.xMax, uvRect.yMin));
			vh.AddTriangle(0 + vertOff, 1 + vertOff, 2 + vertOff);
			vh.AddTriangle(2 + vertOff, 3 + vertOff, 0 + vertOff);
			return 4;
		}


		/// <summary>
		/// Adds a quad after clipping. If the quad does get clipped, the result will be adding 0, 1, or 2, or 3 tris
		/// </summary>
		/// <returns>The number of vertices added.</returns>
		/// <param name="vh">The VertexHelper</param>
		/// <param name="quad">The quad to add (must be length 4)</param>
		/// <param name="clipRect">The clip rect.</param>
		/// <param name="vertOff">The vertex offset for creating tris.</param>
		public static int AddQuadClipped(this VertexHelper vh, UIVertex[] quad, Rect clipRect, int vertOff)
		{
			using(var tri = ArrayPool<UIVertex>.Get(3)) {
				tri.array[0] = quad[0];
				tri.array[1] = quad[1];
				tri.array[2] = quad[2];
				var vAdded = vh.AddTriClipped(tri.array, clipRect, vertOff);
				tri.array[0] = quad[2];
				tri.array[1] = quad[3];
				tri.array[2] = quad[0];
				return vAdded + vh.AddTriClipped(tri.array, clipRect, vertOff + vAdded);
			}
		}

		/// <summary>
		/// Adds a tri after clipping. If the tri does get clipped, the result will be adding 0-3 tris
		/// </summary>
		/// <returns>The number of vertices added. </returns>
		/// <param name="vh">The VertexHelper</param>
		/// <param name="tri">The tri to add (must be length 3)</param>
		/// <param name="clipRect">The clip rect.</param>
		/// <param name="vertOff">The vertex offset for creating tris.</param>
		public static int AddTriClipped(this VertexHelper vh, UIVertex[] tri, Rect clipRect, int vertOff)
		{
			Assert.AreEqual(3, tri.Length);
			using(var inside = ArrayPool<bool>.Get(tri.Length)) {
				var numInside = 0;
				for(int i = 0; i < tri.Length; i++) {
					if((inside.array[i] = clipRect.Contains(tri[i].position))) { 
						numInside++; 
					}
				}
				switch(numInside) {
				case 3: // all inside, just add the tri as passed
					return vh.AddTri(tri, vertOff);
				default:
					using(var clipRectCorners = ArrayPool<Vector2>.Get(4)) {
						clipRect.GetCorners(clipRectCorners.array);
						using(var output = ListPool<UIVertex>.Get()) {
							SutherlandHodgman.GetIntersectedPolygon(tri, clipRectCorners.array, output);
							if(output.Count == 0) {
								return 0;
							}
							if(output.Count < 3) {
								Debug.LogWarning("Invalid polygon vertex count: " + output.Count);
								return 0;
							}
							switch(output.Count) {
							case 3: 
								return vh.AddTri(output, vertOff);
							case 4:
								return vh.AddQuad(output, vertOff);
							default:
								return vh.AddConvexPolygon(output, vertOff);
							}
						}
					}
				}
			}
		}

		private static UIVertex SetColor(this UIVertex uv, Color c)
		{
			uv.color = c;
			return uv;
		}

		public static int AddConvexPolygon(this VertexHelper vh, IList<UIVertex> polygon, int vertOff)
		{
			using(var list = ListPool<UIVertex>.Get()) {
				list.AddRange(polygon);
				var addedVerts = 0;
				// use ear-clipping method: https://en.wikipedia.org/wiki/Polygon_triangulation
				while(list.Count > 4) {
					if(debugging) {
						var c = NextDebugColor();
						for(int i = 0; i < list.Count; i++) {
							list[i] = list[i].SetColor(c);
						}
					}
					addedVerts += vh.AddTri(list[0], list[1], list[list.Count - 1], vertOff + addedVerts);
					list.RemoveAt(0);
				}
				if(debugging) {
					var c1 = NextDebugColor();
					for(int i = 0; i < list.Count; i++) {
						list[i] = list[i].SetColor(c1);
					}
				}	
				return addedVerts + vh.AddQuad(list[0], list[1], list[2], list[3], vertOff + addedVerts);
			}
		}

		public static int AddTri(this VertexHelper vh, IList<UIVertex> tri, int vertOff)
		{
			Assert.AreEqual(3, tri.Count);
			if(debugging) {
				var c = NextDebugColor();
				for(int i = 0; i < 3; i++) {
					tri[i] = tri[i].SetColor(c);
				}
			}
			vh.AddVert(tri[0]); 
			vh.AddVert(tri[1]); 
			vh.AddVert(tri[2]); 
			vh.AddTriangle(0 + vertOff, 1 + vertOff, 2 + vertOff);
			return 3;
		}

		public static int AddTri(this VertexHelper vh, UIVertex v0, UIVertex v1, UIVertex v2, int vertOff)
		{
			if(debugging) {
				var c = NextDebugColor();
				v0.color = c;
				v1.color = c;
				v2.color = c;
			}
			vh.AddVert(v0); 
			vh.AddVert(v1); 
			vh.AddVert(v2); 
			vh.AddTriangle(0 + vertOff, 1 + vertOff, 2 + vertOff);
			return 3;
		}

		public static int AddQuad(this VertexHelper vh, IList<UIVertex> quad, int vertOff)
		{
			Assert.AreEqual(quad.Count, 4);
			if(debugging) {
				var c = NextDebugColor();
				for(int i = 0; i < 4; i++) {
					quad[i] = quad[i].SetColor(c);
				}
			}
			vh.AddVert(quad[0]); 
			vh.AddVert(quad[1]); 
			vh.AddVert(quad[2]); 
			vh.AddVert(quad[3]); 
			vh.AddTriangle(0 + vertOff, 1 + vertOff, 2 + vertOff);
			vh.AddTriangle(2 + vertOff, 3 + vertOff, 0 + vertOff);
			return 4;
		}

		public static int AddQuad(this VertexHelper vh, UIVertex v0, UIVertex v1, UIVertex v2, UIVertex v3, int vertOff)
		{
			if(debugging) {
				var c = NextDebugColor();
				v0.color = c;
				v1.color = c;
				v2.color = c;
				v3.color = c;
			}
			vh.AddVert(v0); 
			vh.AddVert(v1); 
			vh.AddVert(v2); 
			vh.AddVert(v3); 
			vh.AddTriangle(0 + vertOff, 1 + vertOff, 2 + vertOff);
			vh.AddTriangle(2 + vertOff, 3 + vertOff, 0 + vertOff);
			return 4;
		}

		/// <summary>
		/// Call to begin a debugging block. 
		/// VertexHelperExt functions called inside a debug block should draw tris with alternating colors, etc.
		/// 
		/// To end the debug block, either call Requests.DebugEnd 
		/// or you can put the whole debug section in a using block, e.g.
		/// 
		/// 	using(Requests.DebugStart()) {
		/// 		// create and execute a bunch of requests
		/// 	} // debug block ended by IDisposable
		/// </summary>
		public static IDisposable DebugStart(this VertexHelper vh)
		{
			return DebugStart();
		}

		public static IDisposable DebugStart()
		{
			if(debugPinCount == 0) {
				activeDebugColorIndex = DEBUG_COLORS.Length - 1;
			}
			VertextHelperUtils.debugPinCount++;
			return new DebugBlock();
		}

		/// <summary>
		/// Call to end a debugging block. 
		/// Requests created after this call should have their debug property enabled.
		/// NOTE: implemented as a pincount, so debugging really ends 
		/// when the number of DebugEnd calls matches the number of DebugStart
		/// </summary>
		public static void DebugEnd(this VertexHelper vh)
		{
			DebugEnd();
		}

		public static void DebugEnd()
		{
			if(VertextHelperUtils.debugPinCount == 0) {
				return;
			}
			VertextHelperUtils.debugPinCount--;
		}

		/// <summary>
		/// True if a debug block is active. 
		/// When debug is TRUE, new Requests should set their debug property TRUE.
		/// This behaviour is implemented in the constructor for RequestBase
		/// and should be replicated for any Request implementation that doesn't derive from RequestBase.
		/// </summary>
		/// <value><c>true</c> if debugging; otherwise, <c>false</c>.</value>
		public static bool debugging { get { return VertextHelperUtils.debugPinCount > 0; } }

		class DebugBlock : IDisposable
		{
			#region IDisposable implementation
			public void Dispose ()
			{
				VertextHelperUtils.DebugEnd();
			}
			#endregion
		}

		private readonly static Color[] DEBUG_COLORS = { Color.red, Color.magenta, Color.blue, Color.cyan, Color.green, Color.yellow };

		private static Color NextDebugColor() 
		{
			activeDebugColorIndex = (activeDebugColorIndex + 1) % DEBUG_COLORS.Length;
			return activeDebugColor;
		}

		private static Color activeDebugColor { get { return DEBUG_COLORS[activeDebugColorIndex]; } }
		private static int activeDebugColorIndex { get; set; }

		private static int debugPinCount { get; set; }

	}
}





