import { useState } from "react";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Col,
  Offcanvas,
  OffcanvasHeader,
  OffcanvasBody,
} from "reactstrap";

interface OffcanvasItem {
  id: string;
  title: string;
  buttonText: string;
  description: string;
  scroll: boolean;
  backdrop: boolean;
}

const OffcanvasDemo = () => {
  const [openOffcanvas, setOpenOffcanvas] = useState<string | null>(null);

  const offcanvasItems: OffcanvasItem[] = [
    {
      id: "scrolling",
      title: "Offcanvas with body scrolling",
      buttonText: "Enable body scrolling",
      description:
        "Try scrolling the rest of the page to see this option in action.",
      scroll: true,
      backdrop: false,
    },
    {
      id: "staticBackdrop",
      title: "Offcanvas with static backdrop",
      buttonText: "Toggle static offcanvas",
      description: "I will not close if you click outside of me.",
      scroll: false,
      backdrop: true,
    },
    {
      id: "bothOptions",
      title: "Backdrop with scrolling",
      buttonText: "Enable both scrolling & backdrop",
      description:
        "Try scrolling the rest of the page to see this option in action.",
      scroll: true,
      backdrop: true,
    },
  ];

  const handleOpen = (id: string) => {
    setOpenOffcanvas(id);
  };

  const handleClose = () => {
    setOpenOffcanvas(null);
  };

  return (
    <>
      {offcanvasItems.map((item) => (
        <Col md={6} key={item.id}>
          <Card>
            <CardHeader>
              <h5>{item.title}</h5>
              <p>{item.description}</p>
            </CardHeader>
            <CardBody>
              <Button
                color="light-primary"
                className="m-2"
                type="button"
                onClick={() => handleOpen(item.id)}
              >
                {item.buttonText}
              </Button>

              <Offcanvas
                isOpen={openOffcanvas === item.id}
                toggle={handleClose}
                scrollable={item.scroll}
                backdrop={item.backdrop ? "static" : false}
                direction="start"
              >
                <OffcanvasHeader toggle={handleClose}>
                  {item.title}
                </OffcanvasHeader>
                <OffcanvasBody>
                  <p>{item.description}</p>
                </OffcanvasBody>
              </Offcanvas>
            </CardBody>
          </Card>
        </Col>
      ))}
    </>
  );
};

export default OffcanvasDemo;
