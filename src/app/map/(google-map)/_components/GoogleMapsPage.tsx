"use client";
import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Script from "next/script";

const GoogleMaps = () => {
  const mapRef = useRef<HTMLDivElement | null>(null);
  const satelliteMapRef = useRef<HTMLDivElement | null>(null);
  const polygonMapRef = useRef<HTMLDivElement | null>(null);
  const fusionTableMapRef = useRef<HTMLDivElement | null>(null);
  const markerMapRef = useRef<HTMLDivElement | null>(null);
  const overlayMapRef = useRef<HTMLDivElement | null>(null);
  const streetViewMapRef = useRef<HTMLDivElement | null>(null);

  const [scriptLoaded, setScriptLoaded] = useState(false);
  const [overlayPane, setOverlayPane] = useState<HTMLElement | null>(null);

  useEffect(() => {
    if (
      scriptLoaded &&
      typeof window !== "undefined" &&
      (window as any).google
    ) {
      initMaps();
    }
  }, [scriptLoaded]);

  const handleScriptLoad = () => {
    setScriptLoaded(true);
  };

  const initMaps = () => {
    const { google } = window as any;
    if (!google) {
      return;
    }

    if (mapRef.current) {
      new google.maps.Map(mapRef.current, {
        center: { lat: -12.043333, lng: -77.028333 },
        zoom: 12,
        mapTypeId: google.maps.MapTypeId.TERRAIN,
      });
    }

    if (satelliteMapRef.current) {
      new google.maps.Map(satelliteMapRef.current, {
        center: { lat: -12.043333, lng: -77.028333 },
        zoom: 12,
        mapTypeId: google.maps.MapTypeId.SATELLITE,
      });
    }

    if (polygonMapRef.current) {
      const polygonMap = new google.maps.Map(polygonMapRef.current, {
        center: { lat: -12.043333, lng: -77.028333 },
        zoom: 12,
      });
      const path = [
        { lat: -12.040397656836609, lng: -77.03373871559225 },
        { lat: -12.040248585302038, lng: -77.03993927003302 },
        { lat: -12.050047116528843, lng: -77.02448169303511 },
        { lat: -12.044804866577001, lng: -77.02154422636042 },
      ];
      const polygon = new google.maps.Polygon({
        paths: path,
        strokeColor: "#BBD8E9",
        strokeOpacity: 1,
        strokeWeight: 3,
        fillColor: "#BBD8E9",
        fillOpacity: 0.6,
      });
      polygon.setMap(polygonMap);
    }

    if (fusionTableMapRef.current) {
      const fusionTableMap = new google.maps.Map(fusionTableMapRef.current, {
        center: { lat: 37.0902, lng: -95.7129 },
        zoom: 4,
      });

      fusionTableMap.data.loadGeoJson(
        "https://eric.clst.org/assets/wiki/uploads/Stuff/gz_2010_us_040_00_500k.json"
      );

      fusionTableMap.data.setStyle({
        fillColor: "#BBD8E9",
        fillOpacity: 0.6,
        strokeWeight: 1,
      });
    }

    if (markerMapRef.current) {
      const markerMap = new google.maps.Map(markerMapRef.current, {
        center: { lat: -12.043333, lng: -77.028333 },
        zoom: 12,
      });
      new google.maps.Marker({
        position: { lat: -12.043333, lng: -77.028333 },
        title: "Gmap",
        map: markerMap,
      });
    }

    if (overlayMapRef.current) {
      const overlayMap = new google.maps.Map(overlayMapRef.current, {
        center: { lat: -12.043333, lng: -77.028333 },
        zoom: 12,
      });

      const overlay = new google.maps.OverlayView();
      overlay.onAdd = function () {
        const panes = this.getPanes();
        if (panes) {
          setOverlayPane(panes.overlayLayer);
        }
      };
      overlay.onRemove = function () {
        setOverlayPane(null);
      };
      overlay.draw = function () {};
      overlay.setMap(overlayMap);
    }

    if (streetViewMapRef.current) {
      new google.maps.StreetViewPanorama(streetViewMapRef.current, {
        position: { lat: 42.3455, lng: -71.0983 },
        pov: { heading: 100, pitch: 0 },
      });
    }
  };

  return (
    <>
      <Script
        src="https://maps.google.com/maps/api/js"
        strategy="afterInteractive"
        onLoad={handleScriptLoad}
        id="googleMaps"
      />

      <Container fluid>
        <Row className="m-1">
          <Col xs="12">
            <h4 className="main-title">Google Maps</h4>
            <ul className="app-line-breadcrumbs mb-3">
              <li>
                <a href="#" className="f-s-14 f-w-500">
                  <i className="ph-duotone ph-map-pin-line f-s-16" /> Map
                </a>
              </li>
              <li className="active">
                <a href="#" className="f-s-14 f-w-500">
                  Google Maps
                </a>
              </li>
            </ul>
          </Col>
        </Row>

        <Row>
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5>Terrain Type Map</h5>
              </CardHeader>
              <CardBody>
                <div className="w-100 h-400 rounded" ref={mapRef} />
              </CardBody>
            </Card>
          </Col>
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5>Satellite Type Map</h5>
              </CardHeader>
              <CardBody>
                <div className="w-100 h-400 rounded" ref={satelliteMapRef} />
              </CardBody>
            </Card>
          </Col>
        </Row>

        <Row>
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5>Polygons</h5>
              </CardHeader>
              <CardBody>
                <div className="w-100 h-400 rounded" ref={polygonMapRef} />
              </CardBody>
            </Card>
          </Col>
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5>Fusion Tables Layers</h5>
              </CardHeader>
              <CardBody>
                <div className="w-100 h-400 rounded" ref={fusionTableMapRef} />
              </CardBody>
            </Card>
          </Col>
        </Row>

        <Row>
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5>Markers Map</h5>
              </CardHeader>
              <CardBody>
                <div className="w-100 h-400 rounded" ref={markerMapRef} />
              </CardBody>
            </Card>
          </Col>
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5>Overlays Map</h5>
              </CardHeader>
              <CardBody>
                <div className="w-100 h-400 rounded" ref={overlayMapRef} />
                {/* ✅ Portal renders into Google Maps overlay pane */}
                {overlayPane &&
                  createPortal(
                    <div className="map-overlay">
                      Map
                      <div className="overlay-arrow above" />
                    </div>,
                    overlayPane
                  )}
              </CardBody>
            </Card>
          </Col>
        </Row>

        <Row>
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5>Street View Map</h5>
              </CardHeader>
              <CardBody>
                <div className="w-100 h-400 rounded" ref={streetViewMapRef} />
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </>
  );
};

export default GoogleMaps;
