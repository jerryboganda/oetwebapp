"use client";
import React, { useEffect, useRef } from "react";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import SimpleBar from "simplebar";
import { IconBriefcase } from "@tabler/icons-react";

const ScrollbarPage = () => {
  const verticalScrollRef = useRef(null);
  const verticalScrollContentRef = useRef(null);
  const horizontalScrollRef = useRef(null);
  const bothScrollRef = useRef(null);

  useEffect(() => {
    const scrollInstances: SimpleBar[] = [];

    if (verticalScrollRef.current) {
      scrollInstances.push(
        new SimpleBar(verticalScrollRef.current, { autoHide: true })
      );
    }
    if (verticalScrollContentRef.current) {
      scrollInstances.push(
        new SimpleBar(verticalScrollContentRef.current, { autoHide: false })
      );
    }
    if (horizontalScrollRef.current) {
      scrollInstances.push(
        new SimpleBar(horizontalScrollRef.current, { autoHide: false })
      );
    }
    if (bothScrollRef.current) {
      scrollInstances.push(
        new SimpleBar(bothScrollRef.current, { autoHide: false })
      );
    }

    return () => {
      scrollInstances.forEach((instance) => {
        if (instance && instance.unMount) {
          instance.unMount();
        }
      });
    };
  }, []);

  const badges = [
    { name: "Stella Nowland", badgeType: "Freelance" },
    { name: "Lola Stanford", badgeType: "Issue" },
    { name: "Caitlin Coungeau", badgeType: "Social" },
    { name: "Graciela W. McClaran", badgeType: "Issue" },
    { name: "Derek T. Aldridge", badgeType: "Freelance" },
    { name: "Annie A. Riley", badgeType: "Social" },
    { name: "Hana J. Boyd", badgeType: "Issue" },
    { name: "Karen R. Pryce", badgeType: "Freelance" },
    { name: "Annie A. Riley", badgeType: "Social" },
    { name: "Graciela W. McClaran", badgeType: "Issue" },
    { name: "Hana J. Boyd", badgeType: "Freelance" },
    { name: "Stella Nowland", badgeType: "Social" },
  ];

  const images = [
    "/images/profile/07.jpg",
    "/images/profile/09.jpg",
    "/images/profile/10.jpg",
    "/images/profile/05.jpg",
  ];
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Scrollbar"
          title="Advance UI"
          path={["Scrollbar"]}
          Icon={IconBriefcase}
        />

        <Row>
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5>Vertical Scrollbar</h5>
              </CardHeader>
              <CardBody>
                <div
                  className="scroll-bar vertical-scrollbar-box"
                  ref={verticalScrollRef}
                >
                  <h5 className="mb-2">Vertically scrollbar:</h5>
                  <p>
                    To create a vertically scrollable container with detailed
                    control over the scrollbar, you can use a combination of
                    HTML and CSS. Here&#39;s a more detailed example that
                    includes customization of the scrollbar.
                  </p>
                  <img src="/images/blog/02.jpg" alt="" className="w-100" />
                  <ul>
                    <li className="mb-2 mt-2">
                      <span className="f-w-600">Overflow Content:</span> When
                      content within a container exceeds the container’s height,
                      a vertical scrollbar is used to access the hidden content.
                    </li>
                    <li className="mb-2">
                      <span className="f-w-600">Text Blocks:</span> Displaying
                      lengthy articles, documents, or lists of comments.
                    </li>
                    <li className="mb-2">
                      <span className="f-w-600">Data Tables: </span>Viewing
                      large datasets or tables that extend beyond the visible
                      area.
                    </li>
                  </ul>
                </div>
              </CardBody>
            </Card>
          </Col>
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5>Both Scrollbar</h5>
              </CardHeader>
              <CardBody>
                <div className="scroll-bar both-scroll" ref={bothScrollRef}>
                  <img src="/images/blog/09.jpg" alt="" />
                </div>
              </CardBody>
            </Card>
          </Col>
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5>Horizontal Scrollbar</h5>
              </CardHeader>
              <CardBody>
                <div
                  className="scroll-bar horizontal-scrollbar"
                  ref={horizontalScrollRef}
                >
                  <div className="horizontal-content">
                    <div className="row flex-nowrap">
                      {images.map((src, index) => (
                        <div key={index} className="col-4">
                          <div className="horizontal-img">
                            <img
                              src={src}
                              alt={`Image ${index + 1}`}
                              className="img-fluid"
                            />
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                </div>
              </CardBody>
            </Card>
          </Col>
          <Col lg="6">
            <Card className="equal-card">
              <CardHeader>
                <h5>Badges Scrollbar</h5>
              </CardHeader>
              <CardBody>
                <ol
                  className="list-group scroll-bar scroll-list-group"
                  ref={verticalScrollContentRef}
                >
                  {badges.map((badge, index) => (
                    <li
                      key={index}
                      className="list-group-item d-flex justify-content-between align-items-center flex-wrap"
                    >
                      <div className="ms-2">{`${index + 1}. ${badge.name}`}</div>
                      <span
                        className={`badge text-bg-${badge.badgeType.toLowerCase()}`}
                      >
                        {badge.badgeType}
                      </span>
                    </li>
                  ))}
                </ol>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default ScrollbarPage;
