using BeatThat.Pools;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace BeatThat.UIGeometryUtils
{
	public static class SutherlandHodgman
	{
		#region Class: Edge

		/// <summary>
		/// This represents a line segment
		/// </summary>
		private struct Edge
		{
			public Edge(UIVertex p1, UIVertex p2)
			{
				this.p1 = p1;
				this.p2 = p2;
			}

			public readonly UIVertex p1;
			public readonly UIVertex p2;
		}

		#endregion

		/// <summary>
		/// This clips the subject polygon against the clip polygon (gets the intersection of the two polygons)
		/// </summary>
		/// <remarks>
		/// Based on the psuedocode from:
		/// http://en.wikipedia.org/wiki/Sutherland%E2%80%93Hodgman
		/// </remarks>
		/// <param name="subjectPoly">Can be concave or convex</param>
		/// <param name="clipPoly">Must be convex</param>
		/// <param name="output">Results added here</param>
		public static void GetIntersectedPolygon(UIVertex[] subjectPoly, Vector2[] clipPoly, List<UIVertex> output)
		{
			if (subjectPoly.Length < 3 || clipPoly.Length < 3)
			{
				throw new ArgumentException(string.Format("The polygons passed in must have at least 3 points: subject={0}, clip={1}", subjectPoly.Length.ToString(), clipPoly.Length.ToString()));
			}
			using(var clipVertsHolder = ArrayPool<UIVertex>.Get(clipPoly.Length)) {
				// convert the clip poly to an array of UIVertex just so that we can use a common set of IsClockwise, etc. functions with the subject poly

				var clipVerts = clipVertsHolder.array;
				for(int i = 0; i < clipPoly.Length; i++) {
					clipVerts[i].position = clipPoly[i];
				}

				using(var outputWorklist = ListPool<UIVertex>.Get()) {
					outputWorklist.AddRange(subjectPoly);

					//	Make sure it's clockwise
					if (!IsClockwise(subjectPoly)) {
						outputWorklist.Reverse();
					}

					using(var edges = ListPool<Edge>.Get()) {
						
						GetEdgesClockwise(clipVerts, edges);

						//	Walk around the clip polygon clockwise
						foreach (Edge clipEdge in edges) {
							using(var inputList = ListPool<UIVertex>.Get()) {
								inputList.AddRange(outputWorklist);

								outputWorklist.Clear();

								if (inputList.Count == 0)
								{
									//	Sometimes when the polygons don't intersect, this list goes to zero.  Jump out to avoid an index out of range exception
									break;
								}

								var vLast = inputList[inputList.Count - 1];

								foreach (var v in inputList) {

									UIVertex vIntersect;

									if (IsInside(clipEdge, v.position)) {
										if (!IsInside(clipEdge, vLast.position)) {
											
											if(!GetIntersect(vLast, v, clipEdge.p1.position, clipEdge.p2.position, out vIntersect)) {
												throw new ApplicationException("Line segments don't intersect");		//	may be colinear, or may be a bug
											}

											outputWorklist.Add(vIntersect);
										}

										outputWorklist.Add(v);
									}
									else if (IsInside(clipEdge, vLast.position))
									{
										if (!GetIntersect(vLast, v, clipEdge.p1.position, clipEdge.p2.position, out vIntersect)) {
											throw new ApplicationException("Line segments don't intersect");		//	may be colinear, or may be a bug
										}
										outputWorklist.Add(vIntersect);
									}

									vLast = v;
								}
							}
						}
					}

					output.AddRange(outputWorklist);
				}
			}
		}

		#region Private Methods

		/// <summary>
		/// This iterates through the edges of the polygon, always clockwise
		/// </summary>
		[SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
		private static void GetEdgesClockwise(UIVertex[] polygon, IList<Edge> output)
		{
			if (IsClockwise(polygon)) {
				// Already clockwise

				for (int cntr = 0; cntr < polygon.Length - 1; cntr++) {
					output.Add(new Edge(polygon[cntr], polygon[cntr + 1]));
				}

				output.Add(new Edge(polygon[polygon.Length - 1], polygon[0]));
				return;
			}

			// Reverse

			for (int cntr = polygon.Length - 1; cntr > 0; cntr--) {
				output.Add(new Edge(polygon[cntr], polygon[cntr - 1]));
			}

			output.Add(new Edge(polygon[0], polygon[polygon.Length - 1]));
		}

		/// <summary>
		/// Returns the intersection of the two lines (line segments are passed in, but they are treated like infinite lines)
		/// </summary>
		/// <remarks>
		/// Got this here:
		/// http://stackoverflow.com/questions/14480124/how-do-i-detect-triangle-and-rectangle-intersection
		/// </remarks>
		private static bool GetIntersect(UIVertex v1, UIVertex v2, Vector2 clip1, Vector2 clip2, out UIVertex intersect)
		{
			var lineDir = (Vector2)(v2.position - v1.position);
			var clipDir = clip2 - clip1;
			var dotPerp = (lineDir.x * clipDir.y) - (lineDir.y * clipDir.x);

			// If it's 0, it means the lines are parallel so have infinite intersection points
			if (IsNearZero(dotPerp)) {
				intersect = default(UIVertex);
				return false;
			}
				
			var c = clip1 - (Vector2)v1.position;
			var t = (c.x * clipDir.y - c.y * clipDir.x) / dotPerp;

			var newPos = (Vector2)v1.position + (t * lineDir);

			// now calc the uv and vert color of the new vert...
			var len = Vector2.Distance(v1.position, v2.position);
			var pct = (len > 0f)? Vector2.Distance(v1.position, newPos) / len : 1f;

			intersect = UIVertex.simpleVert;

			intersect.position = Vector3.Lerp(v1.position, v2.position, pct); // recalc position here to recover z dimension of pos
			intersect.uv0 = Vector2.Lerp(v1.uv0, v2.uv0, pct);
			intersect.uv1 = Vector2.Lerp(v1.uv1, v2.uv1, pct);
			intersect.color = Color.Lerp(v1.color, v2.color, pct);
			intersect.normal = Vector3.Lerp(v1.normal, v2.normal, pct);
			// not sure if tangent is lerpable?

			return true;
		}

		private static bool IsInside(Edge edge, Vector2 test)
		{
			switch(GetEdgeToPointRelation(edge, test)) {
			case Edge2PointRelation.LEFT:
				return false;
			default:
				return true;
			}
		}

		private static bool IsClockwise(UIVertex[] polygon)
		{
			for (int cntr = 2; cntr < polygon.Length; cntr++)
			{
				switch(GetEdgeToPointRelation(new Edge(polygon[0], polygon[1]), polygon[cntr].position)) {
				case Edge2PointRelation.LEFT:
					return false;
				case Edge2PointRelation.RIGHT:
					return true;
				}
			}

			throw new ArgumentException("All the points in the polygon are colinear");
		}

		private enum Edge2PointRelation { LEFT = -1, COLINEAR = 0, RIGHT = 1,  }

		/// <summary>
		/// Tells if the test point lies on the left side of the edge line
		/// </summary>
		private static Edge2PointRelation GetEdgeToPointRelation(Edge edge, Vector2 test)
		{
			Vector2 tmp1 = edge.p2.position - edge.p1.position;
			Vector2 tmp2 = test - (Vector2)edge.p2.position;

			var x = (tmp1.x * tmp2.y) - (tmp1.y * tmp2.x);		//	dot product of perpendicular?

			if (x < 0) {
				return Edge2PointRelation.RIGHT;
			}

			return x > 0 ? Edge2PointRelation.LEFT : Edge2PointRelation.COLINEAR;
		}

		private static bool IsNearZero(float testValue)
		{
			return Mathf.Approximately(0f, testValue); 
		}

		#endregion
	}
}