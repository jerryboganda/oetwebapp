import { useState } from "react";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Col,
  Offcanvas,
  OffcanvasBody,
  OffcanvasHeader,
} from "reactstrap";

// Type for offcanvas items
interface OffcanvasItem {
  id: string;
  label: string;
  direction: "top" | "end" | "bottom";
}

const PlacementOffcanvasMap = () => {
  // List of all offcanvas items
  const offcanvasItems: OffcanvasItem[] = [
    { id: "top", label: "Top Offcanvas", direction: "top" },
    { id: "right", label: "Right Offcanvas", direction: "end" },
    { id: "bottom", label: "Bottom Offcanvas", direction: "bottom" },
  ];

  // Single state to control which offcanvas is open
  const [openId, setOpenId] = useState<string | null>(null);

  const handleOffcanvas = (id: string) => {
    setOpenId((prev) => (prev === id ? null : id));
  };

  return (
    <Col xs={12}>
      <Card>
        <CardHeader>
          <h5>Placement (No toggle conflict)</h5>
          <p>Offcanvas Different Placement Example: Top, Right & Bottom</p>
        </CardHeader>

        <CardBody className="d-flex flex-wrap gap-2">
          {/* Buttons */}
          {offcanvasItems.map((item) => (
            <Button
              key={item.id}
              color="light-primary"
              className="m-2"
              onClick={() => handleOffcanvas(item.id)}
            >
              Toggle {item.label}
            </Button>
          ))}

          {/* Offcanvas */}
          {offcanvasItems.map((item) => (
            <Offcanvas
              key={item.id}
              isOpen={openId === item.id}
              toggle={() => handleOffcanvas(item.id)}
              direction={item.direction}
            >
              <OffcanvasHeader toggle={() => handleOffcanvas(item.id)}>
                {item.label}
              </OffcanvasHeader>
              <OffcanvasBody>This is the {item.label} content.</OffcanvasBody>
            </Offcanvas>
          ))}
        </CardBody>
      </Card>
    </Col>
  );
};

export default PlacementOffcanvasMap;
