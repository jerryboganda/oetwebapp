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

interface OffcanvasItem {
  id: string;
  label: string;
  body: string;
  scroll: boolean;
  backdrop: boolean;
}

const BackdropOffcanvasMap = () => {
  const offcanvasItems: OffcanvasItem[] = [
    {
      id: "scrolling",
      label: "Colored with scrolling",
      body: "Try scrolling the rest of the page to see this option in action.",
      scroll: true,
      backdrop: false,
    },
    {
      id: "backdrop",
      label: "Offcanvas with backdrop",
      body: "This offcanvas has a backdrop and disables scrolling.",
      scroll: false,
      backdrop: true,
    },
    {
      id: "both",
      label: "Backdroped with scrolling",
      body: "Try scrolling the rest of the page to see this option in action.",
      scroll: true,
      backdrop: true,
    },
  ];

  const [openId, setOpenId] = useState<string | null>(null);

  const handleOpen = (id: string) => {
    setOpenId(id);
  };

  const handleClose = () => {
    setOpenId(null);
  };

  return (
    <Col xs={12}>
      <Card>
        <CardHeader>
          <h5>Backdrop</h5>
          <p>
            Scrolling the <span className="text-danger">body</span> is disabled
            when an offcanvas and its backdrop are visible. Use{" "}
            <span className="text-danger">scroll</span> and{" "}
            <span className="text-danger">backdrop</span> properties to control
            behavior.
          </p>
        </CardHeader>

        <CardBody className="d-flex flex-wrap gap-2">
          {/* Render Buttons */}
          {offcanvasItems.map((item) => (
            <Button
              key={item.id}
              color="light-primary"
              className="m-2"
              onClick={() => handleOpen(item.id)}
            >
              {item.id === "scrolling" && "Enable body scrolling"}
              {item.id === "backdrop" && "Enable backdrop (default)"}
              {item.id === "both" && "Enable both scrolling & backdrop"}
            </Button>
          ))}

          {/* Render Offcanvas */}
          {offcanvasItems.map((item) => (
            <Offcanvas
              key={item.id}
              isOpen={openId === item.id}
              toggle={handleClose}
              scrollable={item.scroll}
              backdrop={item.backdrop}
              direction="start"
            >
              <OffcanvasHeader toggle={handleClose}>
                {item.label}
              </OffcanvasHeader>
              <OffcanvasBody>
                <p>{item.body}</p>
              </OffcanvasBody>
            </Offcanvas>
          ))}
        </CardBody>
      </Card>
    </Col>
  );
};

export default BackdropOffcanvasMap;
