"use client";
import React, { useEffect } from "react";
import { Row, Col, Container } from "reactstrap";
import "glightbox/dist/css/glightbox.css";

const GalleryLayout: React.FC = () => {
  const img = (src: string) => `/images/gallary/${src}`;

  useEffect(() => {
    let lightbox: any;

    const loadLightbox = async () => {
      const GLightbox = (await import("glightbox")).default;
      lightbox = GLightbox({
        selector: ".glightbox",
        touchNavigation: true,
        loop: true,
      });
    };

    loadLightbox();

    return () => {
      if (lightbox) {
        lightbox.destroy();
      }
    };
  }, []);

  return (
    <Container fluid>
      <Row>
        <Col xs="12" className="gallery-grid-container">
          <Row>
            <Col sm="6" lg="4">
              <div className="imagebox">
                <a
                  className="glightbox"
                  data-glightbox="type: image; zoomable: true;"
                  href={img("01.jpg")}
                >
                  <img
                    alt="image"
                    className="img-fluid rounded"
                    src={img("01.jpg")}
                  />
                </a>
                <div className="caption-content">
                  <p>Simple Image</p>
                </div>
              </div>
            </Col>

            <Col sm="6" lg="4">
              <div className="imagebox">
                <a
                  className="glightbox"
                  data-glightbox="title:Description Bottom; description: You can set the position of the description "
                  href={img("02.jpg")}
                >
                  <img
                    alt="image"
                    className="img-fluid rounded"
                    src={img("02.jpg")}
                  />
                </a>
                <div className="caption-content">
                  <p>Image With Bottom Description</p>
                </div>
              </div>
            </Col>

            <Col lg="4">
              <Row>
                {["03.jpg", "04.jpg", "05.jpg", "06.jpg"].map((file, index) => (
                  <Col xs="6" sm="3" lg="6" key={index}>
                    <div className="imagebox">
                      <a
                        className="glightbox"
                        data-glightbox="type: image; zoomable: true;"
                        href={img(file)}
                      >
                        <img
                          alt="image"
                          className="img-fluid w-100 rounded"
                          src={img(file)}
                        />
                      </a>
                      <div className="caption-content">
                        <p>Image With Description</p>
                      </div>
                    </div>
                  </Col>
                ))}
              </Row>
            </Col>

            {["07.jpg", "08.jpg", "09.jpg", "10.jpg"].map((file, index) => (
              <Col xs="6" md="3" key={index}>
                <div className="imagebox">
                  <a
                    className="glightbox"
                    data-glightbox="type: image; zoomable: true;"
                    href={img(file)}
                  >
                    <img
                      alt="image"
                      className="img-fluid rounded"
                      src={img(file)}
                    />
                  </a>
                  <div className="caption-content">
                    <p>Simple Image</p>
                  </div>
                </div>
              </Col>
            ))}

            <Col sm="6">
              <Row>
                {["11.jpg", "12.jpg"].map((file, index) => (
                  <Col xs="6" key={index}>
                    <div className="imagebox">
                      <a
                        className="glightbox"
                        data-glightbox="type: image; zoomable: true;"
                        href={img(file)}
                      >
                        <img
                          alt="image"
                          className="img-fluid rounded"
                          src={img(file)}
                        />
                        {file === "12.jpg" && (
                          <div className="transparent-box2"></div>
                        )}
                      </a>
                      <div className="caption-content">
                        <p>Simple Image</p>
                      </div>
                    </div>
                  </Col>
                ))}
                <Col xs="12">
                  <div className="imagebox">
                    <a
                      className="glightbox"
                      data-glightbox="type: image; zoomable: true;"
                      href={img("14.jpg")}
                    >
                      <img
                        alt="image"
                        className="img-fluid rounded"
                        src={img("14.jpg")}
                      />
                    </a>
                    <div className="caption-content">
                      <p>Simple Image</p>
                    </div>
                  </div>
                </Col>
              </Row>
            </Col>

            <Col sm="6">
              <Row>
                <Col xs="12">
                  <div className="imagebox">
                    <a className="glightbox" href={img("video.mp4")}>
                      <img
                        alt="image"
                        className="img-fluid rounded"
                        src={img("13.jpg")}
                      />
                      <div className="caption-content video-caption">
                        <i className="fa-solid fa-play-circle fa-fw f-s-35"></i>
                      </div>
                    </a>
                  </div>
                </Col>
                {["15.jpg", "16.jpg"].map((file, index) => (
                  <Col xs="6" key={index}>
                    <div className="imagebox">
                      <a
                        className="glightbox"
                        data-glightbox="type: image; zoomable: true;"
                        href={img(file)}
                      >
                        <img
                          alt="image"
                          className="img-fluid rounded"
                          src={img(file)}
                        />
                      </a>
                      <div className="caption-content">
                        <p>Simple Image</p>
                      </div>
                    </div>
                  </Col>
                ))}
              </Row>
            </Col>
          </Row>
        </Col>
      </Row>
    </Container>
  );
};

export default GalleryLayout;
