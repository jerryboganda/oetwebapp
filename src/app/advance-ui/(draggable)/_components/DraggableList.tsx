import React, { useEffect, useRef } from "react";
import { Col, Card, CardBody, Button } from "reactstrap";
import {
  IconBrandFacebook,
  IconBrandTwitter,
  IconBrandWhatsapp,
} from "@tabler/icons-react";
import Sortable from "sortablejs";

const shareMenuItemsLeft = [
  {
    imgSrc: "/images/avatar/1.png",
    name: "Wilson Terry",
    role: "web designer",
    bgColor: "primary",
  },
  {
    imgSrc: "/images/avatar/2.png",
    name: "Jalyn Donnelly",
    role: "web developer",
    bgColor: "secondary",
  },
  {
    imgSrc: "/images/avatar/3.png",
    name: "Betty Mante",
    role: "Ui/ux designer",
    bgColor: "success",
  },
  {
    imgSrc: "/images/avatar/4.png",
    name: "Pinkie Corkery",
    role: "React developer",
    bgColor: "danger",
  },
];

const shareMenuItemsRight = [
  {
    imgSrc: "/images/avatar/5.png",
    name: "Clara Schmidt",
    role: "Php developer",
    bgColor: "info",
  },
  {
    imgSrc: "/images/avatar/5.png",
    name: "Dane Wiza",
    role: "Node js developer",
    bgColor: "warning",
  },
  {
    imgSrc: "/images/avatar/6.png",
    name: "Palma Rohan",
    role: "Ios developer",
    bgColor: "light",
  },
  {
    imgSrc: "/images/avatar/7.png",
    name: "Ora Kreiger",
    role: "Flutter developer",
    bgColor: "dark",
  },
];

const ShareList = () => {
  const shareMenuLeftRef = useRef(null);
  const shareMenuRightRef = useRef(null);

  useEffect(() => {
    if (shareMenuLeftRef.current) {
      new Sortable(shareMenuLeftRef.current, {
        group: "shared",
        animation: 150,
      });
    }
    if (shareMenuRightRef.current) {
      new Sortable(shareMenuRightRef.current, {
        group: "shared",
        animation: 150,
      });
    }
  }, []);

  return (
    <Col xxl="6">
      <div className="card equal-card">
        <div className="card-header">
          <h5>Draggable Share List</h5>
        </div>

        <CardBody>
          <div className="row">
            <Col xs="6" sm="6" md="6" lg="6" className="box-layout-draggable">
              <ul
                className="draggable-share-list share-menu-list"
                ref={shareMenuLeftRef}
              >
                {shareMenuItemsLeft.map((item, index) => (
                  <li key={index}>
                    <Card>
                      <CardBody>
                        <div className="share-menu-item" draggable="false">
                          <div
                            className={`h-40 w-40 d-flex-center b-r-50 overflow-hidden text-bg-${item.bgColor} share-menu-img`}
                          >
                            <img
                              src={item.imgSrc}
                              alt={item.name}
                              className="img-fluid"
                            />
                          </div>
                          <div className="share-menu-content">
                            <h6 className="mb-0">{item.name}</h6>
                            <p className="mb-0 text-secondary">{item.role}</p>
                          </div>
                          <div className="share-menu-icons">
                            <Button
                              type="button"
                              className="btn btn-facebook icon-btn b-r-22"
                            >
                              <IconBrandFacebook
                                size={14}
                                className="text-white"
                              />
                            </Button>{" "}
                            <Button
                              type="button"
                              className="btn btn-twitter icon-btn b-r-22"
                            >
                              <IconBrandTwitter
                                size={14}
                                className="text-white"
                              />
                            </Button>{" "}
                            <Button
                              type="button"
                              className="btn btn-whatsapp icon-btn b-r-22"
                            >
                              <IconBrandWhatsapp
                                size={14}
                                className="text-white"
                              />
                            </Button>
                          </div>
                        </div>
                      </CardBody>
                    </Card>
                  </li>
                ))}
              </ul>
            </Col>
            <Col xs="6" sm="6" md="6" lg="6" className="box-layout-draggable">
              <ul
                className="draggable-share-list share-menu-list"
                ref={shareMenuRightRef}
              >
                {shareMenuItemsRight.map((item, index) => (
                  <li key={index}>
                    <Card>
                      <CardBody>
                        <div className="share-menu-item" draggable="false">
                          <div
                            className={`h-40 w-40 d-flex-center b-r-50 overflow-hidden text-bg-${item.bgColor} share-menu-img`}
                          >
                            <img
                              src={item.imgSrc}
                              alt={item.name}
                              className="img-fluid"
                            />
                          </div>
                          <div className="share-menu-content">
                            <h6 className="mb-0">{item.name}</h6>
                            <p className="mb-0 text-secondary">{item.role}</p>
                          </div>
                          <div className="share-menu-icons">
                            <Button
                              type="button"
                              className="btn btn-facebook icon-btn b-r-22"
                            >
                              <IconBrandFacebook
                                size={14}
                                className="text-white"
                              />
                            </Button>{" "}
                            <Button
                              type="button"
                              className="btn btn-twitter icon-btn b-r-22"
                            >
                              <IconBrandTwitter
                                size={14}
                                className="text-white"
                              />
                            </Button>{" "}
                            <Button
                              type="button"
                              className="btn btn-whatsapp icon-btn b-r-22"
                            >
                              <IconBrandWhatsapp
                                size={14}
                                className="text-white"
                              />
                            </Button>
                          </div>
                        </div>
                      </CardBody>
                    </Card>
                  </li>
                ))}
              </ul>
            </Col>
          </div>
        </CardBody>
      </div>
    </Col>
  );
};

export default ShareList;
