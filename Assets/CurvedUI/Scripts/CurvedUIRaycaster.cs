﻿using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;


namespace CurvedUI
{
    public class CurvedUIRaycaster : GraphicRaycaster
    {

        [SerializeField]
        bool showDebug = false;

        Canvas myCanvas;
        CurvedUISettings mySettings;
        Vector3 cyllinderMidPoint;
        List<GameObject> lastHovered;
        Vector2 lastCanvasPos = Vector2.zero;
        GameObject colliderContainer;

		//gaze selection variables - Experimental
		//GameObject lastSelectedObject;
		//bool lastSelectedClicked = false;
		//float lastSelectedChangedTime;
		//float gazeClickTime = 1.0f;

        //custom settings
        // set this to true if this raycaster should use a copy of the event data. 
        // Overriding eventData allows canvas to use 1:1 scrolling. Scroll rects and sliders behave as they should on a curved surface and follow the pointer.
        // This however breaks the interactions with flat canvases in the same scene and eventData will not be correct for them any more. 
        // Settings this to false will make the canvas create a copy of the eventData that is going to be used for finding objects under pointer.
        // Flat canvases on the same scene will work fine, but scroll rects on curved canvases will move faster / slower than the pointer.
        //
        //Warning: may break dragging and scrolling as there will be no past pointereventdata to calculate delta position from.
        //
        // default true.
        bool overrideEventData = true; 
        
        #region LIFECYCLE
        protected override void Awake()
        {
            base.Awake();
            myCanvas = GetComponent<Canvas>();
            mySettings = GetComponent<CurvedUISettings>();

            cyllinderMidPoint = new Vector3(0, 0, -mySettings.GetCyllinderRadiusInCanvasSpace());

            //the canvas needs an event camera set up to process events correctly. Try to use main camera if no one is provided.
            if (myCanvas.worldCamera == null && Camera.main != null)
                myCanvas.worldCamera = Camera.main;
        }

        protected override void Start()
        {
            CreateCollider();
        }
        #endregion


        #region RAYCASTING
        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
        {

            if (!mySettings.Interactable)
                return;

            //check if we have a world camera to process events by
            if (myCanvas.worldCamera == null)
                Debug.LogWarning("CurvedUIRaycaster requires Canvas to have a world camera reference to process events!", myCanvas.gameObject);

            Camera worldCamera = myCanvas.worldCamera;
            Ray ray3D;

            //get a ray to raycast with depending on the control method
            switch (CurvedUIInputModule.Controller)
            {
                case CurvedUIInputModule.CurvedUIController.MOUSE:
                {
                    // Get a ray from the camera through the point on the screen - used for mouse input
                    ray3D = worldCamera.ScreenPointToRay(eventData.position);
                    break;
                }
                case CurvedUIInputModule.CurvedUIController.GAZE:
                {
                    //get a ray from the center of world camera. used for gaze input
                    ray3D = new Ray(worldCamera.transform.position, worldCamera.transform.forward);

                    bool selectableUnderGaze = false;

                    //find if our selected object is still under gaze
                    foreach (GameObject go in eventData.hovered)
                    {
                        if (go == eventData.selectedObject)
                        {
                            selectableUnderGaze = true;
                            break;
                        }
                    }

                    //deselect if its not
                    if (!selectableUnderGaze)
                        eventData.selectedObject = null;

                    foreach (GameObject go in eventData.hovered)
                    {
                        if (go == null)
                            continue;

                        Graphic gph = go.GetComponent<Graphic>();

                        //go through only go that can be selected and are drawn by the canvas
#if UNITY_5_1
                    if (go.GetComponent<Selectable>() != null && gph != null && gph.depth != -1)
#else
                        if (go.GetComponent<Selectable>() != null && gph != null && gph.depth != -1 && gph.raycastTarget)
#endif
                        {
                            if (eventData.selectedObject != go)
                                eventData.selectedObject = go;

                            break;
                        }
                    }

                    //Test for selected object being dragged and initialize dragging, if needed.
                    //We do this here to trick unity's StandAloneInputModule into thinking we used a touch or mouse to do it.
                    if (eventData.IsPointerMoving() && eventData.pointerDrag != null
                        && !eventData.dragging
                        && ShouldStartDrag(eventData.pressPosition, eventData.position, EventSystem.current.pixelDragThreshold, eventData.useDragThreshold))
                    {
                        ExecuteEvents.Execute(eventData.pointerDrag, eventData, ExecuteEvents.beginDragHandler);
                        eventData.dragging = true;
                    }

//					// Experimental - Execute click after hovering gaze on an object for few seconds.
//					if(lastSelectedObject != eventData.selectedObject){
//						lastSelectedObject = eventData.selectedObject;
//						lastSelectedChangedTime = Time.time;
//						lastSelectedClicked = false;
//						Debug.Log("last selected changed");
//
//					}
//					if(Time.time - lastSelectedChangedTime > gazeClickTime && !lastSelectedClicked){
//						lastSelectedClicked = true;
//						ExecuteEvents.Execute(eventData.selectedObject, eventData, ExecuteEvents.pointerClickHandler);
//						Debug.Log("gaze click");
//					}
//

                    break;
                }
                case CurvedUIInputModule.CurvedUIController.WORLD_MOUSE:
                {
                    // Get a ray set in CustromControllerRay property
                    ray3D = new Ray(worldCamera.transform.position, (mySettings.CanvasToCurvedCanvas(CurvedUIInputModule.Instance.WorldSpaceMouseInCanvasSpace) - myCanvas.worldCamera.transform.position));
                    break;
                }
                case CurvedUIInputModule.CurvedUIController.VIVE:
                {
                    // Get a ray from right controller.
#if CURVEDUI_VIVE
                    ray3D = new Ray((eventData as CurvedUIPointerEventData).Controller.transform.position, (eventData as CurvedUIPointerEventData).Controller.transform.forward);

                    break;
#else
                    goto case CurvedUIInputModule.CurvedUIController.CUSTOM_RAY;
#endif
                }
                case CurvedUIInputModule.CurvedUIController.OCULUS_TOUCH:
                {
                    goto case CurvedUIInputModule.CurvedUIController.CUSTOM_RAY;
                }
                case CurvedUIInputModule.CurvedUIController.CUSTOM_RAY:
                {
                    // Get a ray set in CustromControllerRay property
                    ray3D = CurvedUIInputModule.CustomControllerRay;
                    ProcessMove(eventData);
                    break;
                }
                case CurvedUIInputModule.CurvedUIController.DAYDREAM:
                {
                    goto case CurvedUIInputModule.CurvedUIController.CUSTOM_RAY;
                }
				case CurvedUIInputModule.CurvedUIController.GOOGLEVR:
				{
					goto case CurvedUIInputModule.CurvedUIController.GAZE;
				}
                default:
                {
                    ray3D = new Ray();
                    break;
                }
            }

           
            //copy eventdata to be used by this
            PointerEventData newEventData = new PointerEventData(EventSystem.current);
            if (!overrideEventData) {
                newEventData.pointerEnter = eventData.pointerEnter;
                newEventData.rawPointerPress = eventData.rawPointerPress;
                newEventData.pointerDrag = eventData.pointerDrag;
                newEventData.pointerCurrentRaycast = eventData.pointerCurrentRaycast;
                newEventData.pointerPressRaycast = eventData.pointerPressRaycast;
                newEventData.hovered = new List<GameObject>();
                newEventData.hovered.AddRange(eventData.hovered);
                newEventData.eligibleForClick = eventData.eligibleForClick;
                newEventData.pointerId = eventData.pointerId;
                newEventData.position = eventData.position;
                newEventData.delta = eventData.delta;
                newEventData.pressPosition = eventData.pressPosition;
                newEventData.clickTime = eventData.clickTime;
                newEventData.clickCount = eventData.clickCount;
                newEventData.scrollDelta = eventData.scrollDelta;
                newEventData.useDragThreshold = eventData.useDragThreshold;
                newEventData.dragging = eventData.dragging;
                newEventData.button = eventData.button;
            }
              


            if (mySettings.Angle != 0 && mySettings.enabled)
            { // use custom raycasting only if Curved effect is enabled
                Vector2 remappedPosition = eventData.position;

                //Test only this object's layer if settings require it.
                int myLayerMask = -1;
                if (mySettings.RaycastMyLayerOnly)
                {
                    myLayerMask = 1 << this.gameObject.layer;
                }

                switch (mySettings.Shape)
                {
                    //find if we hit anything, if not, do nothing
                    case CurvedUISettings.CurvedUIShape.CYLINDER:
                    {
                        if (!RaycastToCyllinderCanvas(ray3D, out remappedPosition, false, myLayerMask)) return;
                        break;
                    }
                    case CurvedUISettings.CurvedUIShape.CYLINDER_VERTICAL:
                    {
                        if (!RaycastToCyllinderVerticalCanvas(ray3D, out remappedPosition, false, myLayerMask)) return;
                        break;
                    }
                    case CurvedUISettings.CurvedUIShape.RING:
                    {
                        if (!RaycastToRingCanvas(ray3D, out remappedPosition, false, myLayerMask)) return;
                        break;
                    }
                    case CurvedUISettings.CurvedUIShape.SPHERE:
                    {
                        if (!RaycastToSphereCanvas(ray3D, out remappedPosition, false, myLayerMask)) return;
                        break;
                    }
                }

                if (overrideEventData)
                {
                    // Update event data
                    eventData.position = remappedPosition;

                    //update delta for vive
                    if (CurvedUIInputModule.Controller == CurvedUIInputModule.CurvedUIController.VIVE)
                    {
                        eventData.delta = remappedPosition - lastCanvasPos;
                        lastCanvasPos = remappedPosition;
                    }

                } else
                {
                    // Update event data
                    newEventData.position = remappedPosition;

                    //update delta for vive
                    if (CurvedUIInputModule.Controller == CurvedUIInputModule.CurvedUIController.VIVE)
                    {
                        newEventData.delta = remappedPosition - lastCanvasPos;
                        lastCanvasPos = remappedPosition;
                    }
                }

            }

            if (overrideEventData)
            {
                //store objects under pointer so they can quickly retrieved if needed by other scripts
                lastHovered = eventData.hovered;

                // Use base class raycast method to finish the raycast if we hit anything
                base.Raycast(eventData, resultAppendList);

            } else
            {
                //store objects under pointer so they can quickly retrieved if needed by other scripts
                lastHovered = newEventData.hovered;

                // Use base class raycast method to finish the raycast if we hit anything
                base.Raycast(newEventData, resultAppendList);
            }
                
        }



        public virtual bool RaycastToCyllinderCanvas(Ray ray3D, out Vector2 o_canvasPos, bool OutputInCanvasSpace = false, int myLayerMask = -1)
        {

            if (showDebug)
            {
                Debug.DrawLine(ray3D.origin, ray3D.GetPoint(1000), Color.red);
            }

            RaycastHit hit = new RaycastHit();
            if (Physics.Raycast(ray3D, out hit, float.PositiveInfinity, myLayerMask))
            {
                //find if we hit this canvas - this needs to be uncommented
                if (overrideEventData && hit.collider.gameObject != this.gameObject && (colliderContainer == null || hit.collider.transform.parent != colliderContainer.transform))
                {
                    o_canvasPos = Vector2.zero;
                    return false;
                }

                //direction from the cyllinder center to the hit point
                Vector3 localHitPoint = myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(hit.point);
                Vector3 directionFromCyllinderCenter = (localHitPoint - cyllinderMidPoint).normalized;

                //angle between middle of the projected canvas and hit point direction
                float angle = -AngleSigned(directionFromCyllinderCenter.ModifyY(0), mySettings.Angle < 0 ? Vector3.back : Vector3.forward, Vector3.up);

                //convert angle to canvas coordinates
                Vector2 canvasSize = myCanvas.GetComponent<RectTransform>().rect.size;

                //map the intersection point to 2d point in canvas space
                Vector2 pointOnCanvas = new Vector3(0, 0, 0);
                pointOnCanvas.x = angle.Remap(-mySettings.Angle / 2.0f, mySettings.Angle / 2.0f, -canvasSize.x / 2.0f, canvasSize.x / 2.0f);
                pointOnCanvas.y = localHitPoint.y;


                if (OutputInCanvasSpace)
                    o_canvasPos = pointOnCanvas;
                else //convert the result to screen point in camera. This will be later used by raycaster and world camera to determine what we're pointing at
                    o_canvasPos = myCanvas.worldCamera.WorldToScreenPoint(myCanvas.transform.localToWorldMatrix.MultiplyPoint3x4(pointOnCanvas));

                if (showDebug)
                {
                    //Debug.DrawLine(canvasWorldPoint, canvasWorldPoint.ModifyY(canvasWorldPoint.y + 10), Color.blue);
                    Debug.DrawLine(hit.point, hit.point.ModifyY(hit.point.y + 10), Color.green);
                    Debug.DrawLine(hit.point, myCanvas.transform.localToWorldMatrix.MultiplyPoint3x4(cyllinderMidPoint), Color.yellow);
                }

                return true;
            }

            o_canvasPos = Vector2.zero;
            return false;
        }

        public virtual bool RaycastToCyllinderVerticalCanvas(Ray ray3D, out Vector2 o_canvasPos, bool OutputInCanvasSpace = false, int myLayerMask = -1)
        {

            if (showDebug)
            {
                Debug.DrawLine(ray3D.origin, ray3D.GetPoint(1000), Color.red);
            }

            RaycastHit hit = new RaycastHit();
            if (Physics.Raycast(ray3D, out hit, float.PositiveInfinity, myLayerMask))
            {
                //find if we hit this canvas
                if (overrideEventData && hit.collider.gameObject != this.gameObject && (colliderContainer == null || hit.collider.transform.parent != colliderContainer.transform))
                {
                    o_canvasPos = Vector2.zero;
                    return false;
                }

                //direction from the cyllinder center to the hit point
                Vector3 localHitPoint = myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(hit.point);
                Vector3 directionFromCyllinderCenter = (localHitPoint - cyllinderMidPoint).normalized;

                //angle between middle of the projected canvas and hit point direction
                float angle = -AngleSigned(directionFromCyllinderCenter.ModifyX(0), mySettings.Angle < 0 ? Vector3.back : Vector3.forward, Vector3.left);

                //convert angle to canvas coordinates
                Vector2 canvasSize = myCanvas.GetComponent<RectTransform>().rect.size;

                //map the intersection point to 2d point in canvas space
                Vector2 pointOnCanvas = new Vector3(0, 0, 0);
                pointOnCanvas.y = angle.Remap(-mySettings.Angle / 2.0f, mySettings.Angle / 2.0f, -canvasSize.y / 2.0f, canvasSize.y / 2.0f);
                pointOnCanvas.x = localHitPoint.x;


                if (OutputInCanvasSpace)
                    o_canvasPos = pointOnCanvas;
                else //convert the result to screen point in camera. This will be later used by raycaster and world camera to determine what we're pointing at
                    o_canvasPos = myCanvas.worldCamera.WorldToScreenPoint(myCanvas.transform.localToWorldMatrix.MultiplyPoint3x4(pointOnCanvas));

                if (showDebug)
                {
                    //Debug.DrawLine(canvasWorldPoint, canvasWorldPoint.ModifyY(canvasWorldPoint.y + 10), Color.blue);
                    Debug.DrawLine(hit.point, hit.point.ModifyY(hit.point.y + 10), Color.green);
                    Debug.DrawLine(hit.point, myCanvas.transform.localToWorldMatrix.MultiplyPoint3x4(cyllinderMidPoint), Color.yellow);
                }

                return true;
            }

            o_canvasPos = Vector2.zero;
            return false;
        }

        public virtual bool RaycastToRingCanvas(Ray ray3D, out Vector2 o_canvasPos, bool OutputInCanvasSpace = false, int myLayerMask = -1)
        {

            RaycastHit hit = new RaycastHit();
            if (Physics.Raycast(ray3D, out hit, float.PositiveInfinity, myLayerMask))
            {
                //find if we hit this canvas
                if (overrideEventData && hit.collider.gameObject != this.gameObject && (colliderContainer == null || hit.collider.transform.parent != colliderContainer.transform))
                {
                    o_canvasPos = Vector2.zero;
                    return false;
                }

                //local hit point on canvas and a direction from center
                Vector3 localHitPoint = myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(hit.point);
                Vector3 directionFromRingCenter = localHitPoint.ModifyZ(0).normalized;

                Vector2 canvasSize = myCanvas.GetComponent<RectTransform>().rect.size;

                //angle between middle of the projected canvas and hit point direction from center
                float angle = -AngleSigned(directionFromRingCenter.ModifyZ(0), Vector3.up, Vector3.back);

                //map the intersection point to 2d point in canvas space
                Vector2 pointOnCanvas = new Vector2(0, 0);

                if (showDebug)
                    Debug.Log("angle: " + angle);

                //map x coordinate based on angle between vector up and direction to hitpoint
                if (angle < 0)
                {
                    pointOnCanvas.x = angle.Remap(0, -mySettings.Angle, -canvasSize.x / 2.0f, canvasSize.x / 2.0f);
                }
                else {
                    pointOnCanvas.x = angle.Remap(360, 360 - mySettings.Angle, -canvasSize.x / 2.0f, canvasSize.x / 2.0f);
                }

                //map y coordinate based on hitpoint distance from the center and external diameter
                pointOnCanvas.y = localHitPoint.magnitude.Remap(mySettings.RingExternalDiameter * 0.5f * (1 - mySettings.RingFill), mySettings.RingExternalDiameter * 0.5f,
                    -canvasSize.y * 0.5f * (mySettings.RingFlipVertical ? -1 : 1), canvasSize.y * 0.5f * (mySettings.RingFlipVertical ? -1 : 1));


                if (OutputInCanvasSpace)
                    o_canvasPos = pointOnCanvas;
                else //convert the result to screen point in camera. This will be later used by raycaster and world camera to determine what we're pointing at
                    o_canvasPos = myCanvas.worldCamera.WorldToScreenPoint(myCanvas.transform.localToWorldMatrix.MultiplyPoint3x4(pointOnCanvas));
                return true;
            }

            o_canvasPos = Vector2.zero;
            return false;
        }


        public virtual bool RaycastToSphereCanvas(Ray ray3D, out Vector2 o_canvasPos, bool OutputInCanvasSpace = false, int myLayerMask = -1)
        {

            RaycastHit hit = new RaycastHit();
            if (Physics.Raycast(ray3D, out hit, float.PositiveInfinity, myLayerMask))
            {
                //find if we hit this canvas
                if (overrideEventData && hit.collider.gameObject != this.gameObject && (colliderContainer == null || hit.collider.transform.parent != colliderContainer.transform))
                {
                    o_canvasPos = Vector2.zero;
                    return false;
                }

                Vector2 canvasSize = myCanvas.GetComponent<RectTransform>().rect.size;
                float radius = (mySettings.PreserveAspect ? mySettings.GetCyllinderRadiusInCanvasSpace() : canvasSize.x / 2.0f);

                //local hit point on canvas, direction from its center and a vector perpendicular to direction, so we can use it to calculate its angle in both planes.
                Vector3 localHitPoint = myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(hit.point);
                Vector3 SphereCenter = new Vector3(0, 0, mySettings.PreserveAspect ? -radius : 0);
                Vector3 directionFromSphereCenter = (localHitPoint - SphereCenter).normalized;
                Vector3 XZPlanePerpendicular = Vector3.Cross(directionFromSphereCenter, directionFromSphereCenter.ModifyY(0)).normalized * (directionFromSphereCenter.y < 0 ? 1 : -1);

                //horizontal and vertical angle between middle of the sphere and the hit point.
                //We do some fancy checks to determine vectors we compare them to,
                //to make sure they are negative on the left and bottom side of the canvas
                float hAngle = -AngleSigned(directionFromSphereCenter.ModifyY(0), (mySettings.Angle > 0 ? Vector3.forward : Vector3.back), (mySettings.Angle > 0 ? Vector3.up : Vector3.down));
                float vAngle = -AngleSigned(directionFromSphereCenter, directionFromSphereCenter.ModifyY(0), XZPlanePerpendicular);

                //find the size of the canvas expressed as measure of the arc it occupies on the sphere
                float hAngularSize = Mathf.Abs(mySettings.Angle) * 0.5f;
                float vAngularSize = Mathf.Abs(mySettings.PreserveAspect ? hAngularSize * canvasSize.y / canvasSize.x : mySettings.VerticalAngle * 0.5f);

                //map the intersection point to 2d point in canvas space
                Vector2 pointOnCanvas = new Vector2(hAngle.Remap(-hAngularSize, hAngularSize, -canvasSize.x * 0.5f, canvasSize.x * 0.5f),
                                                    vAngle.Remap(-vAngularSize, vAngularSize, -canvasSize.y * 0.5f, canvasSize.y * 0.5f));

                if (showDebug)
                {
                    Debug.Log("h: " + hAngle + " / v: " + vAngle + " poc: " + pointOnCanvas);
                    Debug.DrawRay(myCanvas.transform.localToWorldMatrix.MultiplyPoint3x4(SphereCenter), myCanvas.transform.localToWorldMatrix.MultiplyVector(directionFromSphereCenter) * Mathf.Abs(radius), Color.red);
                    Debug.DrawRay(myCanvas.transform.localToWorldMatrix.MultiplyPoint3x4(SphereCenter), myCanvas.transform.localToWorldMatrix.MultiplyVector(XZPlanePerpendicular) * 300, Color.magenta);
                }

                if (OutputInCanvasSpace)
                    o_canvasPos = pointOnCanvas;
                else // convert the result to screen point in camera.This will be later used by raycaster and world camera to determine what we're pointing at
                    o_canvasPos = myCanvas.worldCamera.WorldToScreenPoint(myCanvas.transform.localToWorldMatrix.MultiplyPoint3x4(pointOnCanvas));

                return true;
            }

            o_canvasPos = Vector2.zero;
            return false;
        }
        #endregion



        #region COLLIDER MANAGEMENT

        /// <summary>
        /// Creates a mesh collider for curved canvas based on current angle and curve segments.
        /// </summary>
        /// <returns>The collider.</returns>
        protected void CreateCollider()
        {

            //remove all colliders on this object
            List<Collider> Cols = new List<Collider>();
            Cols.AddRange(this.GetComponents<Collider>());
            for (int i = 0; i < Cols.Count; i++)
            {
                Destroy(Cols[i]);
            }

            if (!mySettings.BlocksRaycasts) return; //null;

            if (mySettings.Shape == CurvedUISettings.CurvedUIShape.SPHERE && !mySettings.PreserveAspect && mySettings.VerticalAngle == 0) return;// null;

            //create a collider based on mapping type
            switch (mySettings.Shape)
            {

                case CurvedUISettings.CurvedUIShape.CYLINDER:
                {

                    //creating a convex (lower performance - many parts) collider for when we have a rigidbody attached
                    if (GetComponent <Rigidbody>() != null || GetComponentInParent<Rigidbody>() != null)
                    {
                        if (colliderContainer != null)
                            GameObject.Destroy(colliderContainer);

                         colliderContainer = CreateConvexCyllinderCollider();
                    }
                    else // create a faster single mesh collier when possible
                    {
                        //create a meshfilter if this object does not have one yet.
                        if (GetComponent<MeshFilter>() == null)
                            this.gameObject.AddComponent<MeshFilter>();
                       
                        MeshFilter mf = GetComponent<MeshFilter>();
                        MeshCollider mc = this.gameObject.AddComponent<MeshCollider>();
                        Mesh meshie = CreateCyllinderColliderMesh();
                        mf.mesh = meshie;
                        mc.sharedMesh = meshie;
//						//ADDED
						mc.isTrigger = true;
                    }
                    return;
                }
                case CurvedUISettings.CurvedUIShape.CYLINDER_VERTICAL:
                {
                    //creating a convex (lower performance - many parts) collider for when we have a rigidbody attached
                    if (GetComponent<Rigidbody>() != null || GetComponentInParent<Rigidbody>() != null)
                    {
                        if (colliderContainer != null)
                            GameObject.Destroy(colliderContainer);

                        colliderContainer = CreateConvexCyllinderCollider(true);
                    }
                    else // create a faster single mesh collier when possible
                    {
                        //create a meshfilter if this object does not have one yet.
                        if (GetComponent<MeshFilter>() == null)
                            this.gameObject.AddComponent<MeshFilter>();

                        MeshFilter mf = GetComponent<MeshFilter>();
                        MeshCollider mc = this.gameObject.AddComponent<MeshCollider>();
                        Mesh meshie = CreateCyllinderColliderMesh(true);
                        mf.mesh = meshie;
                        mc.sharedMesh = meshie;
						mc.isTrigger = true;
                    }
                    return;
                }

                case CurvedUISettings.CurvedUIShape.RING:
                {

                    BoxCollider col = this.gameObject.AddComponent<BoxCollider>();
                    col.size = new Vector3(mySettings.RingExternalDiameter, mySettings.RingExternalDiameter, 1.0f);
					col.isTrigger = true;
                    return;
                }

                case CurvedUISettings.CurvedUIShape.SPHERE:
                {

                    //create a meshfilter if this object does not have one yet.
                    if (GetComponent<MeshFilter>() == null)
                    {
                        this.gameObject.AddComponent<MeshFilter>();
                    }

                    MeshFilter mf = GetComponent<MeshFilter>();
                    MeshCollider mc = this.gameObject.AddComponent<MeshCollider>();

                    Mesh meshie = CreateSphereColliderMesh();

                    mf.mesh = meshie;
                    mc.sharedMesh = meshie;
					mc.isTrigger = true;
                    return;
                }
                default:
                {
                    return;
                }
            }

        }



        GameObject CreateConvexCyllinderCollider(bool vertical = false)
        {

            GameObject go = new GameObject("_CurvedUIColliders");
            go.layer = this.gameObject.layer;
            go.transform.SetParent(this.transform);
            go.transform.ResetTransform();

            Mesh meshie = new Mesh();

            Vector3[] Vertices = new Vector3[4];
            (myCanvas.transform as RectTransform).GetWorldCorners(Vertices);
            meshie.vertices = Vertices;

            //rearrange them to be in an easy to interpolate order and convert to canvas local spce
            if (vertical)
            {
                Vertices[0] = myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[1]);
                Vertices[1] = myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[2]);
                Vertices[2] = myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[0]);
                Vertices[3] = myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[3]);
            }
            else
            {
                Vertices[0] = myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[1]);
                Vertices[1] = myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[0]);
                Vertices[2] = myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[2]);
                Vertices[3] = myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[3]);
            }

            meshie.vertices = Vertices;

            //create a new array of vertices, subdivided as needed
            List<Vector3> verts = new List<Vector3>();
            int vertsCount = Mathf.Max(8, Mathf.RoundToInt(mySettings.BaseCircleSegments * Mathf.Abs(mySettings.Angle) / 360.0f));

            for (int i = 0; i < vertsCount; i++)
            {
                verts.Add(Vector3.Lerp(meshie.vertices[0], meshie.vertices[2], (i * 1.0f) / (vertsCount - 1)));
            }

            //curve the verts in canvas local space
            if (mySettings.Angle != 0)
            {
                Rect canvasRect = myCanvas.GetComponent<RectTransform>().rect;
                float radius = mySettings.GetCyllinderRadiusInCanvasSpace();

                for (int i = 0; i < verts.Count; i++)
                {

                    Vector3 newpos = verts[i];
                    if (vertical)
                    {
                        float theta = (verts[i].y / canvasRect.size.y) * mySettings.Angle * Mathf.Deg2Rad;
                        newpos.y = Mathf.Sin(theta) * radius;
                        newpos.z += Mathf.Cos(theta) * radius - radius;
                        verts[i] = newpos;
                    }
                    else
                    {
                        float theta = (verts[i].x / canvasRect.size.x) * mySettings.Angle * Mathf.Deg2Rad;
                        newpos.x = Mathf.Sin(theta) * radius;
                        newpos.z += Mathf.Cos(theta) * radius - radius;
                        verts[i] = newpos;
                    }
                }
            }


            //create our box colliders and arrange them in a nice cyllinder
            for(int i = 0; i < verts.Count-1; i++)
            {
                GameObject newBox = new GameObject("Box collider");
                newBox.transform.SetParent(go.transform );
                newBox.transform.ResetTransform();
				//Added
                BoxCollider c = newBox.AddComponent<BoxCollider>();
				c.isTrigger = true;

                if (vertical) 
                {
                    newBox.transform.localPosition = new Vector3(0, (verts[i + 1].y + verts[i].y) * 0.5f, (verts[i + 1].z + verts[i].z) * 0.5f);
                    newBox.transform.localScale = new Vector3(0.1f, Vector3.Distance(Vertices[0], Vertices[1]), Vector3.Distance(verts[i + 1], verts[i]));
                    newBox.transform.localRotation = Quaternion.LookRotation((verts[i + 1] - verts[i]), Vertices[0] - Vertices[1]);
                } else
                {
                    newBox.transform.localPosition = new Vector3((verts[i + 1].x + verts[i].x) * 0.5f, 0, (verts[i + 1].z + verts[i].z) * 0.5f);
                    newBox.transform.localScale = new Vector3(0.1f, Vector3.Distance(Vertices[0], Vertices[1]), Vector3.Distance(verts[i + 1], verts[i]));
                    newBox.transform.localRotation = Quaternion.LookRotation((verts[i + 1] - verts[i]), Vertices[0] - Vertices[1]);
                }

            }

            return go;

        }

        Mesh CreateCyllinderColliderMesh(bool vertical = false)
        {

            Mesh meshie = new Mesh();

            Vector3[] Vertices = new Vector3[4];
            (myCanvas.transform as RectTransform).GetWorldCorners(Vertices);
            meshie.vertices = Vertices;

            //rearrange them to be in an easy to interpolate order and convert to canvas local spce
            if (vertical)
            {
                Vertices[0] = myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[1]);
                Vertices[1] = myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[2]);
                Vertices[2] = myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[0]);
                Vertices[3] = myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[3]);
            }
            else
            {
                Vertices[0] = myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[1]);
                Vertices[1] = myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[0]);
                Vertices[2] = myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[2]);
                Vertices[3] = myCanvas.transform.worldToLocalMatrix.MultiplyPoint3x4(meshie.vertices[3]);
            }

            meshie.vertices = Vertices;

            //create a new array of vertices, subdivided as needed
            List<Vector3> verts = new List<Vector3>();
            int vertsCount = Mathf.Max(8, Mathf.RoundToInt(mySettings.BaseCircleSegments * Mathf.Abs(mySettings.Angle) / 360.0f));


            for (int i = 0; i < vertsCount; i++)
            {
                verts.Add(Vector3.Lerp(meshie.vertices[0], meshie.vertices[2], (i * 1.0f) / (vertsCount - 1)));
                verts.Add(Vector3.Lerp(meshie.vertices[1], meshie.vertices[3], (i * 1.0f) / (vertsCount - 1)));
            }


            //curve the verts in canvas local space
            if (mySettings.Angle != 0)
            {
                Rect canvasRect = myCanvas.GetComponent<RectTransform>().rect;
                float radius = GetComponent<CurvedUISettings>().GetCyllinderRadiusInCanvasSpace();

                for (int i = 0; i < verts.Count; i++)
                {

                    Vector3 newpos = verts[i];
                    if (vertical)
                    {
                        float theta = (verts[i].y / canvasRect.size.y) * mySettings.Angle * Mathf.Deg2Rad;
                        newpos.y = Mathf.Sin(theta) * radius;
                        newpos.z += Mathf.Cos(theta) * radius - radius;
                        verts[i] = newpos;
                    }
                    else
                    {
                        float theta = (verts[i].x / canvasRect.size.x) * mySettings.Angle * Mathf.Deg2Rad;
                        newpos.x = Mathf.Sin(theta) * radius;
                        newpos.z += Mathf.Cos(theta) * radius - radius;
                        verts[i] = newpos;
                    }


                }
            }

            meshie.vertices = verts.ToArray();

            //create triangles drom verts
            List<int> tris = new List<int>();
            for (int i = 0; i < verts.Count / 2 - 1; i++)
            {
                if (vertical)
                {
                    //forward tris
                    tris.Add(i * 2 + 0);
                    tris.Add(i * 2 + 1);
                    tris.Add(i * 2 + 2);

                    tris.Add(i * 2 + 1);
                    tris.Add(i * 2 + 3);
                    tris.Add(i * 2 + 2);
                }
                else {
                    //forward tris
                    tris.Add(i * 2 + 2);
                    tris.Add(i * 2 + 1);
                    tris.Add(i * 2 + 0);

                    tris.Add(i * 2 + 2);
                    tris.Add(i * 2 + 3);
                    tris.Add(i * 2 + 1);
                }
            }
            meshie.triangles = tris.ToArray();

            return meshie;
        }

        Mesh CreateSphereColliderMesh()
        {

            Mesh meshie = new Mesh();

            Vector3[] Corners = new Vector3[4];
            (myCanvas.transform as RectTransform).GetWorldCorners(Corners);

            List<Vector3> verts = new List<Vector3>(Corners);
            for (int i = 0; i < verts.Count; i++)
            {
                verts[i] = mySettings.transform.worldToLocalMatrix.MultiplyPoint3x4(verts[i]);
            }

            if (mySettings.Angle != 0)
            {
                // Tesselate quads and apply transformation
                int startingVertexCount = verts.Count;
                for (int i = 0; i < startingVertexCount; i += 4)
                    ModifyQuad(verts, i, mySettings.GetTesslationSize(true));

                // Remove old quads
                verts.RemoveRange(0, startingVertexCount);

                //curve verts
                float vangle = mySettings.VerticalAngle;
                float cylinder_angle = mySettings.Angle;
                Vector2 canvasSize = (myCanvas.transform as RectTransform).rect.size;
                float radius = mySettings.GetCyllinderRadiusInCanvasSpace();

                //caluclate vertical angle for aspect - consistent mapping
                if (mySettings.PreserveAspect)
                {
                    vangle = mySettings.Angle * (canvasSize.y / canvasSize.x);
                }
                else {//if we're not going for constant aspect, set the width of the sphere to equal width of the original canvas
                    radius = canvasSize.x / 2.0f;
                }

                //curve the vertices 
                for (int i = 0; i < verts.Count; i++)
                {

                    float theta = (verts[i].x / canvasSize.x).Remap(-0.5f, 0.5f, (180 - cylinder_angle) / 2.0f - 90, 180 - (180 - cylinder_angle) / 2.0f - 90);
                    theta *= Mathf.Deg2Rad;
                    float gamma = (verts[i].y / canvasSize.y).Remap(-0.5f, 0.5f, (180 - vangle) / 2.0f, 180 - (180 - vangle) / 2.0f);
                    gamma *= Mathf.Deg2Rad;

                    verts[i] = new Vector3(Mathf.Sin(gamma) * Mathf.Sin(theta) * radius,
                        -radius * Mathf.Cos(gamma),
                        Mathf.Sin(gamma) * Mathf.Cos(theta) * radius + (mySettings.PreserveAspect ? -radius : 0));
                }
            }
            meshie.vertices = verts.ToArray();

            //create triangles from verts
            List<int> tris = new List<int>();
            for (int i = 0; i < verts.Count; i += 4)
            {
                tris.Add(i + 0);
                tris.Add(i + 1);
                tris.Add(i + 2);

                tris.Add(i + 3);
                tris.Add(i + 0);
                tris.Add(i + 2);
            }


            meshie.triangles = tris.ToArray();
            return meshie;
        }


        #endregion


        #region SUPPORT FUNCTIONS
        /// <summary>
        /// Determine the signed angle between two vectors, with normal 'n'
        /// as the rotation axis.
        /// </summary>
        float AngleSigned(Vector3 v1, Vector3 v2, Vector3 n)
        {
            return Mathf.Atan2(
                Vector3.Dot(n, Vector3.Cross(v1, v2)),
                Vector3.Dot(v1, v2)) * Mathf.Rad2Deg;
        }

        private bool ShouldStartDrag(Vector2 pressPos, Vector2 currentPos, float threshold, bool useDragThreshold)
        {
            if (!useDragThreshold)
                return true;

            return (pressPos - currentPos).sqrMagnitude >= threshold * threshold;
        }

        protected virtual void ProcessMove(PointerEventData pointerEvent)
        {
            var targetGO = pointerEvent.pointerCurrentRaycast.gameObject;
            HandlePointerExitAndEnter(pointerEvent, targetGO);
        }

        // walk up the tree till a common root between the last entered and the current entered is foung
        // send exit events up to (but not inluding) the common root. Then send enter events up to
        // (but not including the common root).
        protected void HandlePointerExitAndEnter(PointerEventData currentPointerData, GameObject newEnterTarget)
        {
            // if we have no target / pointerEnter has been deleted
            // just send exit events to anything we are tracking
            // then exit
            if (newEnterTarget == null || currentPointerData.pointerEnter == null)
            {
                for (var i = 0; i < currentPointerData.hovered.Count; ++i)
                    ExecuteEvents.Execute(currentPointerData.hovered[i], currentPointerData, ExecuteEvents.pointerExitHandler);

                currentPointerData.hovered.Clear();

                if (newEnterTarget == null)
                {
                    currentPointerData.pointerEnter = newEnterTarget;
                    return;
                }
            }

            // if we have not changed hover target
            if (currentPointerData.pointerEnter == newEnterTarget && newEnterTarget)
                return;

            GameObject commonRoot = FindCommonRoot(currentPointerData.pointerEnter, newEnterTarget);

            // and we already an entered object from last time
            if (currentPointerData.pointerEnter != null)
            {
                // send exit handler call to all elements in the chain
                // until we reach the new target, or null!
                Transform t = currentPointerData.pointerEnter.transform;

                while (t != null)
                {
                    // if we reach the common root break out!
                    if (commonRoot != null && commonRoot.transform == t)
                        break;

                    ExecuteEvents.Execute(t.gameObject, currentPointerData, ExecuteEvents.pointerExitHandler);
                    currentPointerData.hovered.Remove(t.gameObject);
                    t = t.parent;
                }
            }

            // now issue the enter call up to but not including the common root
            currentPointerData.pointerEnter = newEnterTarget;
            if (newEnterTarget != null)
            {
                Transform t = newEnterTarget.transform;

                while (t != null && t.gameObject != commonRoot)
                {
                    ExecuteEvents.Execute(t.gameObject, currentPointerData, ExecuteEvents.pointerEnterHandler);
                    currentPointerData.hovered.Add(t.gameObject);
                    t = t.parent;
                }
            }
        }

        protected static GameObject FindCommonRoot(GameObject g1, GameObject g2)
        {
            if (g1 == null || g2 == null)
                return null;

            var t1 = g1.transform;
            while (t1 != null)
            {
                var t2 = g2.transform;
                while (t2 != null)
                {
                    if (t1 == t2)
                        return t1.gameObject;
                    t2 = t2.parent;
                }
                t1 = t1.parent;
            }
            return null;
        }

		/// <summary>
		/// REturns a screen point under which a ray intersects the curved canvas in its event camera view
		/// </summary>
		/// <returns><c>true</c>, if screen space point by ray was gotten, <c>false</c> otherwise.</returns>
		/// <param name="ray">Ray.</param>
		/// <param name="o_positionOnCanvas">O position on canvas.</param>
		bool GetScreenSpacePointByRay(Ray ray, out Vector2 o_positionOnCanvas){

			switch (mySettings.Shape)
			{
			case CurvedUISettings.CurvedUIShape.CYLINDER:
				{
					return RaycastToCyllinderCanvas(ray, out o_positionOnCanvas, false);
				}
			case CurvedUISettings.CurvedUIShape.CYLINDER_VERTICAL:
				{
					return RaycastToCyllinderVerticalCanvas(ray, out o_positionOnCanvas, false);
				}
			case CurvedUISettings.CurvedUIShape.RING:
				{
					return RaycastToRingCanvas(ray, out o_positionOnCanvas, false);
				}
			case CurvedUISettings.CurvedUIShape.SPHERE:
				{
					return RaycastToSphereCanvas(ray, out o_positionOnCanvas, false);
				}
			default:
				{
					 o_positionOnCanvas = Vector2.zero;
					return false;
				}
			}

		}
        #endregion





        #region PUBLIC

        public void RebuildCollider()
        {
            cyllinderMidPoint = new Vector3(0, 0, -mySettings.GetCyllinderRadiusInCanvasSpace());
            CreateCollider();
        }

		/// <summary>
		/// Returns all objects currently under the pointer
		/// </summary>
		/// <returns>The objects under pointer.</returns>
        public List<GameObject> GetObjectsUnderPointer()
        {
            if (lastHovered == null) lastHovered = new List<GameObject>();
            return lastHovered;
        }

		/// <summary>
		/// Returns all the canvas objects that are visible under given Screen Position
		/// </summary>
		/// <returns>The objects under screen position.</returns>
		/// <param name="screenPos">Screen position.</param>
		/// <param name="eventCamera">Event camera.</param>
		public List<GameObject> GetObjectsUnderScreenPos(Vector2 screenPos, Camera eventCamera = null){
			if (eventCamera == null)
				eventCamera = myCanvas.worldCamera;

			return GetObjectsHitByRay (eventCamera.ScreenPointToRay (screenPos));
		}

		/// <summary>
		/// Returns all the canvas objects that are intersected by given ray
		/// </summary>
		/// <returns>The objects hit by ray.</returns>
		/// <param name="ray">Ray.</param>
		public List<GameObject> GetObjectsHitByRay(Ray ray){
			List<GameObject> results = new List<GameObject> ();

			Vector2 pointerPosition;

			//ray outside the canvas, return null
			if(!GetScreenSpacePointByRay(ray, out pointerPosition))
				return results;

			//lets find the graphics under ray!
			List<Graphic> s_SortedGraphics = new List<Graphic>();
			var foundGraphics = GraphicRegistry.GetGraphicsForCanvas(myCanvas);
			for (int i = 0; i < foundGraphics.Count; ++i)
			{
				Graphic graphic = foundGraphics[i];

				// -1 means it hasn't been processed by the canvas, which means it isn't actually drawn
				if (graphic.depth == -1 || !graphic.raycastTarget)
					continue;

				if (!RectTransformUtility.RectangleContainsScreenPoint(graphic.rectTransform, pointerPosition, eventCamera))
					continue;

				if (graphic.Raycast(pointerPosition, eventCamera))
				{
					s_SortedGraphics.Add(graphic);
				}
			}

			s_SortedGraphics.Sort((g1, g2) => g2.depth.CompareTo(g1.depth));
			//		StringBuilder cast = new StringBuilder();
			for (int i = 0; i < s_SortedGraphics.Count; ++i)
				results.Add(s_SortedGraphics[i].gameObject);
			//		Debug.Log (cast.ToString());

			s_SortedGraphics.Clear();

			return results;
		}
        #endregion



        #region TESSELATION
        void ModifyQuad(List<Vector3> verts, int vertexIndex, Vector2 requiredSize)
        {

            // Read the existing quad vertices
            List<Vector3> quad = new List<Vector3>();
            for (int i = 0; i < 4; i++)
                quad.Add(verts[vertexIndex + i]);

            // horizotal and vertical directions of a quad. We're going to tesselate parallel to these.
            Vector3 horizontalDir = quad[2] - quad[1];
            Vector3 verticalDir = quad[1] - quad[0];

            // Find how many quads we need to create
            int horizontalQuads = Mathf.CeilToInt(horizontalDir.magnitude * (1.0f / Mathf.Max(1.0f, requiredSize.x)));
            int verticalQuads = Mathf.CeilToInt(verticalDir.magnitude * (1.0f / Mathf.Max(1.0f, requiredSize.y)));

            // Create the quads!
            float yStart = 0.0f;
            for (int y = 0; y < verticalQuads; ++y)
            {

                float yEnd = (y + 1.0f) / verticalQuads;
                float xStart = 0.0f;

                for (int x = 0; x < horizontalQuads; ++x)
                {
                    float xEnd = (x + 1.0f) / horizontalQuads;

                    //Add new quads to list
                    verts.Add(TesselateQuad(quad, xStart, yStart));
                    verts.Add(TesselateQuad(quad, xStart, yEnd));
                    verts.Add(TesselateQuad(quad, xEnd, yEnd));
                    verts.Add(TesselateQuad(quad, xEnd, yStart));

                    //begin the next quad where we ened this one
                    xStart = xEnd;
                }
                //begin the next row where we ended this one
                yStart = yEnd;
            }
        }





        Vector3 TesselateQuad(List<Vector3> quad, float x, float y)
        {

            Vector3 ret = Vector3.zero;

            //1. calculate weighting factors
            List<float> weights = new List<float>(){
            (1-x) * (1-y),
            (1-x) * y,
            x * y,
            x * (1-y),
        };

            //2. interpolate pos using weighting factors
            for (int i = 0; i < 4; i++)
            {
                ret += quad[i] * weights[i];
            }
            return ret;
        }

        #endregion

    }
}
