import React, { useEffect, useRef } from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { IconMapPin } from "@tabler/icons-react";
import "leaflet/dist/leaflet.css";
import markerIcon2x from "leaflet/dist/images/marker-icon-2x.png";
import markerIcon from "leaflet/dist/images/marker-icon.png";
import markerShadow from "leaflet/dist/images/marker-shadow.png";

interface MapConfig {
  id: string;
  title: string;
  options: L.MapOptions;
  initMap?: (
    L: typeof import("leaflet"),
    map: L.Map,
    defaultMarkerIcon: L.Icon
  ) => void;
}

const mapConfigs: MapConfig[] = [
  {
    id: "mobilemap",
    title: "Leaflet on Mobile Map",
    options: { center: [0, 0], zoom: 2 },
  },
  {
    id: "accessiblemap",
    title: "Markers, Circles and Polygons",
    options: { center: [51.505, -0.09], zoom: 13 },
    initMap: (L, map, defaultMarkerIcon) => {
      L.marker([51.5, -0.09], { icon: defaultMarkerIcon })
        .bindPopup("<b>Hello!</b><br>I am a popup.")
        .addTo(map);
      L.circle([51.508, -0.1], {
        fillColor: "#467FFB",
        fillOpacity: 0.6,
        radius: 500,
      })
        .bindPopup("This is a circle.")
        .addTo(map);
    },
  },
  {
    id: "markersmap",
    title: "Accessible Maps",
    options: { center: [50.4501, 30.5234], zoom: 4 },
    initMap: (L, map, defaultMarkerIcon) => {
      L.marker([50.4501, 30.5234], { icon: defaultMarkerIcon })
        .bindPopup("Kyiv, Ukraine is the birthplace of Leaflet!")
        .addTo(map);
    },
  },
  {
    id: "interactivemap",
    title: "Interactive Choropleth Map",
    options: { center: [37.8, -96], zoom: 4 },
  },
  {
    id: "customiconsmap",
    title: "Markers with Custom Icons",
    options: { center: [51.5, -0.09], zoom: 13 },
    initMap: (L, map) => {
      const greenIcon = new L.Icon({
        iconUrl: "https://leafletjs.com/examples/custom-icons/leaf-green.png",
        shadowUrl:
          "https://leafletjs.com/examples/custom-icons/leaf-shadow.png",
        iconSize: [38, 95],
        shadowSize: [50, 64],
        iconAnchor: [22, 94],
        shadowAnchor: [4, 62],
        popupAnchor: [-3, -76],
      });

      const redIcon = new L.Icon({
        iconUrl: "https://leafletjs.com/examples/custom-icons/leaf-red.png",
        shadowUrl:
          "https://leafletjs.com/examples/custom-icons/leaf-shadow.png",
        iconSize: [38, 95],
        shadowSize: [50, 64],
        iconAnchor: [22, 94],
        shadowAnchor: [4, 62],
        popupAnchor: [-3, -76],
      });

      L.marker([51.5, -0.09], { icon: greenIcon })
        .bindPopup("I am green.")
        .addTo(map);
      L.marker([51.495, -0.083], { icon: redIcon })
        .bindPopup("I am red.")
        .addTo(map);
    },
  },
  {
    id: "layersmap",
    title: "Layer Groups and Layers Control",
    options: { center: [-29.5, 145], zoom: 3.5 },
    initMap: (L, map) => {
      const basemaps = {
        StreetView: L.tileLayer(
          "https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
        ),
        Topography: L.tileLayer.wms(
          "http://ows.mundialis.de/services/service?",
          {
            layers: "TOPO-WMS",
          }
        ),
      };
      L.control.layers(basemaps).addTo(map);
      basemaps.StreetView.addTo(map);
    },
  },
];

const resolveMarkerAsset = (asset: string | { src: string }) =>
  typeof asset === "string" ? asset : asset.src;

const LeafletMapPage = () => {
  const mapRefs = useRef<Record<string, L.Map | null>>({});
  const containerRefs = useRef<Record<string, HTMLDivElement | null>>({});

  useEffect(() => {
    let isMounted = true;
    const mapInstances = mapRefs.current;

    const initializeMaps = async () => {
      const L = await import("leaflet");
      const defaultMarkerIcon = L.icon({
        iconRetinaUrl: resolveMarkerAsset(markerIcon2x),
        iconUrl: resolveMarkerAsset(markerIcon),
        shadowUrl: resolveMarkerAsset(markerShadow),
        iconSize: [25, 41],
        iconAnchor: [12, 41],
        popupAnchor: [1, -34],
        shadowSize: [41, 41],
      });

      if (!isMounted) return;

      mapConfigs.forEach((config) => {
        const container = containerRefs.current[config.id];
        if (container && !mapInstances[config.id]) {
          const map = L.map(container, config.options);

          L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
            maxZoom: 19,
            attribution: "&copy; OpenStreetMap contributors",
          }).addTo(map);

          config.initMap?.(L, map, defaultMarkerIcon);
          mapInstances[config.id] = map;
        }
      });
    };

    initializeMaps();

    return () => {
      isMounted = false;
      // Cleanup all map instances
      Object.entries(mapInstances).forEach(([id, map]) => {
        if (map) {
          map.remove();
          mapInstances[id] = null;
        }
      });
    };
  }, []);

  return (
    <Container fluid>
      <Breadcrumbs
        mainTitle="Leaflet Map"
        title="Map"
        path={["Leaflet Map"]}
        Icon={IconMapPin}
      />
      <Row>
        {mapConfigs.map((config) => (
          <Col lg="6" key={config.id}>
            <Card>
              <CardHeader>
                <h5>{config.title}</h5>
              </CardHeader>
              <CardBody>
                <div
                  ref={(el) => {
                    containerRefs.current[config.id] = el;
                  }}
                  className="leaflet-map-container w-full h-280"
                />
              </CardBody>
            </Card>
          </Col>
        ))}
      </Row>
    </Container>
  );
};

export default LeafletMapPage;
