import React from "react";
import {
  Card,
  CardBody,
  CardFooter,
  CardHeader,
  CardImg,
  CardText,
  CardTitle,
  Col,
  Row,
} from "reactstrap";
import { IconHeartFilled } from "@tabler/icons-react";

const topBottomImageCards = [
  {
    id: "card-1",
    position: "top" as const,
    image: "/images/blog/06.jpg",
    title: "Card Title",
    content:
      "This is a wider card with supporting text below as a natural lead-in to.",
  },
  {
    id: "card-2",
    position: "bottom" as const,
    image: "/images/blog/02.jpg",
    title: "Card Title",
    content:
      "This is a wider card with supporting text below as a natural lead-in to additional This content.",
  },
];

const avatarImages = [
  "/images/avatar/4.png",
  "/images/avatar/1.png",
  "/images/avatar/2.png",
  "/images/avatar/3.png",
];

const avatarBgClasses = [
  "text-bg-danger",
  "text-bg-success",
  "text-bg-warning",
  "text-bg-info",
];

const listItems = ["An item", "A second item", "A third item", "A Fourth item"];

const CardStyle = () => {
  return (
    <>
      {topBottomImageCards.map(({ id, position, image, title, content }) => (
        <Col md="6" xxl="4" key={id}>
          <Card className="hover-effect">
            {position === "top" && (
              <CardImg src={image} className="card-img-top" alt="..." />
            )}
            <CardBody>
              <CardTitle tag="h5">{title}</CardTitle>
              <CardText>{content}</CardText>
              <CardText>
                <small className="text-body-secondary">
                  Last updated 3 min&#39;s ago
                </small>
              </CardText>
            </CardBody>
            {position === "bottom" && (
              <CardImg src={image} className="card-img-bottom" alt="..." />
            )}
          </Card>
        </Col>
      ))}

      <Col xxl="4">
        <Row>
          <Col xxl="12" md="6">
            <Card className="hover-effect">
              <CardHeader>
                <h6 className="mb-0 mt-2 f-w-600">My Profile</h6>
              </CardHeader>
              <CardBody>
                <p>
                  I am a keen, hard working, reliable and excellent time keeper.
                  I am a bright and receptive person
                </p>
              </CardBody>
              <CardFooter>
                <Row>
                  <div className="col-6">
                    <IconHeartFilled
                      size={16}
                      className="text-danger f-s-16 m-r-5"
                    />
                    <span>60 likes</span>
                  </div>
                  <div className="col-6">
                    <ul className="avatar-group float-end">
                      {avatarImages.map((src, i) => (
                        <li
                          key={i}
                          className={`h-25 w-25 d-flex-center b-r-50 ${avatarBgClasses[i]} b-2-light position-relative`}
                          title="Sabrina Torres"
                        >
                          <CardImg
                            src={src}
                            alt=""
                            className="img-fluid b-r-50 overflow-hidden"
                          />
                        </li>
                      ))}
                      <li
                        className="text-bg-primary h-25 w-25 d-flex-center b-r-50"
                        title="5 More"
                      >
                        5+
                      </li>
                    </ul>
                  </div>
                </Row>
              </CardFooter>
            </Card>
          </Col>
          <Col xxl="12" md="6">
            <Card className="hover-effect">
              <CardHeader>Featured</CardHeader>
              <ul className="list-group list-group-flush">
                {listItems.map((item, idx) => (
                  <li key={idx} className="list-group-item">
                    {item}
                  </li>
                ))}
              </ul>
            </Card>
          </Col>
        </Row>
      </Col>

      <Col xl="6">
        <Card className=" mb-3 hover-effect">
          <Row>
            <Col md="6" xl="4">
              <CardImg
                src="/images/blog/08.jpg"
                className="img-fluid"
                alt="..."
              />
            </Col>
            <Col md="6" xl="8">
              <CardBody>
                <CardTitle tag="h5">Card Title</CardTitle>
                <CardText>
                  This is a wider card with supporting text below as a natural
                  lead-in to additional content. This content is a little bit
                  longer.
                </CardText>
                <CardText>
                  <small className="text-body-secondary">
                    Last updated 3 min&#39;s ago
                  </small>
                </CardText>
              </CardBody>
            </Col>
          </Row>
        </Card>
      </Col>
    </>
  );
};

export default CardStyle;
