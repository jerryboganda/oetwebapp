import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";

import {
  IconAlignCenter,
  IconAlignLeft,
  IconAlignRight,
  IconBriefcase,
  IconLayoutAlignMiddle,
} from "@tabler/icons-react";

const AlignmentThingPage = () => {
  const items = [
    {
      className: "top-center",
      icon: <IconAlignCenter size={40} />,
      label: "top-center",
    },
    {
      className: "center",
      icon: <IconAlignCenter size={40} />,
      label: "center",
    },
    {
      className: "bottom-center",
      icon: <IconAlignCenter size={40} />,
      label: "bottom-center",
    },
    {
      className: "left-center",
      icon: <IconAlignLeft size={40} />,
      label: "left-center",
    },
    {
      className: "right-center",
      icon: <IconAlignRight size={40} />,
      label: "right-center",
    },
    {
      className: "top-left",
      icon: <IconAlignLeft size={40} />,
      label: "top-left",
    },
    {
      className: "top-right",
      icon: <IconAlignRight size={40} />,
      label: "top-right",
    },
    {
      className: "bottom-left",
      icon: <IconAlignLeft size={40} />,
      label: "bottom-left",
    },
    {
      className: "bottom-right",
      icon: <IconAlignRight size={40} />,
      label: "bottom-right",
    },
    {
      className: "center-horizontal",
      icon: <IconLayoutAlignMiddle size={40} />,
      label: "center-horizontal",
    },
    {
      className: "center-vertical",
      icon: <IconLayoutAlignMiddle size={40} />,
      label: "center-vertical",
    },
  ];

  const imagePositions = [
    { className: "image-top-left", label: "image-top-left" },
    { className: "image-center", label: "image-center" },
    { className: "image-bottom-right", label: "image-bottom-right" },
    { className: "image-top-right", label: "image-top-right" },
    { className: "image-bottom-left", label: "image-bottom-left" },
  ];
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Alignment Thing"
          title="Ui Kits"
          path={["Alignment Thing"]}
          Icon={IconBriefcase}
        />
        <Col xs={12}>
          <Card>
            <CardHeader className="code-header">
              <h5>Text Alignment</h5>
            </CardHeader>

            <CardBody>
              <Row className="center-thing-responsive">
                {items.map((item, index) => (
                  <Col key={index} xs={4} md={3} xxl={2}>
                    <div className="center-thing mt-3">
                      <div className={item.className}>{item.icon}</div>
                    </div>
                    <p className="f-s-16 text-center m-2">{item.label}</p>
                  </Col>
                ))}
              </Row>
            </CardBody>
          </Card>
        </Col>
        <Col xs={12}>
          <Card>
            <CardHeader className="code-header">
              <h5>Image alignment</h5>
            </CardHeader>

            <CardBody>
              <Col xl={12}>
                <Row>
                  {imagePositions.map((position, index) => (
                    <Col key={index} xl={3} className="mb-3">
                      <div className="image-center-thing">
                        <div className={position.className}>
                          <img
                            src="/images/placeholder/05.png"
                            alt=""
                            className="rounded"
                          />
                        </div>
                      </div>
                      <p className="f-s-16 text-center m-2">{position.label}</p>
                    </Col>
                  ))}
                </Row>
              </Col>
            </CardBody>
          </Card>
        </Col>
      </Container>
    </div>
  );
};

export default AlignmentThingPage;
