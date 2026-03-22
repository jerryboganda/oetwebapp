import React, { useRef, useState } from "react";
import { Card, CardBody, CardHeader, Col, Row } from "reactstrap";

const ScrollpyNested: React.FC = () => {
  const scrollRef = useRef<HTMLDivElement>(null);
  const [activeId, setActiveId] = useState("item-1");

  const handleScroll = () => {
    if (!scrollRef.current) return;
    const scrollTop = scrollRef.current.scrollTop;
    const children = scrollRef.current.children;

    for (let i = 0; i < children.length; i++) {
      const child = children[i] as HTMLElement;
      const offsetTop = child.offsetTop;
      const offsetHeight = child.offsetHeight;

      if (
        scrollTop + 50 >= offsetTop &&
        scrollTop + 50 < offsetTop + offsetHeight
      ) {
        setActiveId(child.id);
        break;
      }
    }
  };

  const handleClick = (id: string) => {
    setActiveId(id);
    const target = document.getElementById(id);
    target?.scrollIntoView({ behavior: "smooth" });
  };

  return (
    <Col xs="12">
      <Row>
        <Col sm={4}>
          <Card>
            <CardHeader>
              <h5>Scrollpy with nested nav</h5>
            </CardHeader>
            <CardBody>
              <div
                id="navbar-example3"
                className="h-100 flex-column align-items-stretch pe-4 scrollpy-nested-nav"
              >
                <div className="nav nav-pills flex-column">
                  <button
                    className={`nav-link nav-pill-primary ${activeId === "item-1" ? "active" : ""}`}
                    onClick={() => handleClick("item-1")}
                  >
                    Item 1
                  </button>
                  <div className="nav nav-pills flex-column ms-3">
                    <button
                      className={`nav-link nav-pill-primary ms-3 my-1 ${activeId === "item-1-1" ? "active" : ""}`}
                      onClick={() => handleClick("item-1-1")}
                    >
                      Item 1.1
                    </button>
                    <button
                      className={`nav-link nav-pill-primary ms-3 my-1 ${activeId === "item-1-2" ? "active" : ""}`}
                      onClick={() => handleClick("item-1-2")}
                    >
                      Item 1.2
                    </button>
                  </div>
                  <button
                    className={`nav-link nav-pill-primary ${activeId === "item-2" ? "active" : ""}`}
                    onClick={() => handleClick("item-2")}
                  >
                    Item 2
                  </button>
                  <button
                    className={`nav-link nav-pill-primary ${activeId === "item-3" ? "active" : ""}`}
                    onClick={() => handleClick("item-3")}
                  >
                    Item 3
                  </button>
                  <div className="nav nav-pills flex-column ms-3">
                    <button
                      className={`nav-link nav-pill-primary ms-3 my-1 ${activeId === "item-3-1" ? "active" : ""}`}
                      onClick={() => handleClick("item-3-1")}
                    >
                      Item 3.1
                    </button>
                    <button
                      className={`nav-link nav-pill-primary ms-3 my-1 ${activeId === "item-3-2" ? "active" : ""}`}
                      onClick={() => handleClick("item-3-2")}
                    >
                      Item 3.2
                    </button>
                  </div>
                </div>
              </div>
            </CardBody>
          </Card>
        </Col>
        <Col sm={8}>
          <Card>
            <CardBody>
              <div
                ref={scrollRef}
                onScroll={handleScroll}
                className="scrollspy-example-2 h-350 overflow-y-scroll app-scroll"
              >
                <div id="item-1">
                  <h5 className="f-w-500 mb-2 text-dark">Item 1</h5>
                  <p className="f-s-15 text-secondary mg-b-14">
                    A web designer is a skilled professional who specializes in
                    creating visually appealing and functional websites. They
                    possess a strong understanding of design principles, user
                    experience, and coding languages to develop websites that
                    are both aesthetically pleasing and user-friendly. A web
                    designer is responsible for creating the overall look and
                    feel of a
                  </p>
                </div>
                <div id="item-1-1">
                  <h5 className="f-w-500 mb-2 text-dark">Item 1.1</h5>
                  <p className="f-s-15 text-secondary mg-b-14">
                    The role of a web designer goes beyond just creating
                    visually appealing websites. They also need to have a deep
                    understanding of user experience (UX) design principles.
                    This involves considering how users will interact with the
                    website, ensuring that it is easy to navigate, and
                    optimizing it for different devices and screen sizes. A web
                    designer needs to have a keen eye for detail and be able to
                    create designs
                  </p>
                </div>
                <div id="item-1-2">
                  <h5 className="f-w-500 mb-2 text-dark"> Item 1.2</h5>
                  <p className="f-s-15 text-secondary mg-b-14">
                    It is a long-established fact that a reader will be
                    distracted by the readable content of a page when looking at
                    its layout. The point of using Lorem Ipsum is that it has a
                    more-or-less normal distribution of letters, as opposed to
                    using &#39;Content here, content here&#39;, making it look
                    like readable English. Many desktop publishing packages and
                    web page editors now use Lorem Ipsum as their default model
                    text, and a search for &#39;lorem ipsum will uncover many
                    websites still in their infancy. Various versions have
                    evolved over the years, sometimes by accident, sometimes on
                    purpose injected humour and the like
                  </p>
                </div>
                <div id="item-2">
                  <h5 className="f-w-500 mb-2 text-dark">Item 2</h5>
                  <p className="f-s-15 text-secondary mg-b-14">
                    In addition to design skills, a web designer also needs to
                    have a solid understanding of coding languages such as HTML,
                    CSS, and JavaScript. This allows them to bring their designs
                    to life and ensure that the website functions as intended.
                    They need to be able to write clean and efficient code,
                    optimize the website for search engines, and ensure that it
                    is compatible with different browsers and devices.
                  </p>
                </div>
                <div id="item-3">
                  <h5 className="f-w-500 mb-2 text-dark">Item 3</h5>
                  <p className="f-s-15 text-secondary mg-b-14">
                    {" "}
                    A web designer is a creative professional who specializes in
                    designing and creating visually appealing and user-friendly
                    websites. They have a deep understanding of various design
                    principles, color schemes, typography, and layout techniques
                    to create a visually stunning website that aligns with the
                    client&#39;s brand and objectives. A web designer combines
                    their technical skills with their artistic flair to bring a
                    website
                  </p>
                </div>
                <div id="item-3-1">
                  <h5 className="f-w-500 mb-2 text-dark">Item 3.1</h5>
                  <p className="f-s-15 text-secondary mg-b-14">
                    {" "}
                    The role of a web designer goes beyond just creating a
                    visually appealing website. They also need to have a good
                    understanding of user experience (UX) design principles.
                    This involves considering how users will interact with the
                    website, ensuring that it is easy to navigate, and that the
                    information is presented in a logical and intuitive manner.
                    A web designer will also need to have a good understanding
                    of responsive design, ensuring that the website looks and
                    functions well on different devices and screen sizes.
                  </p>
                </div>
                <div id="item-3-2">
                  <h5 className="f-w-500 mb-2 text-dark">Item 3.2</h5>
                  <p className="f-s-15 text-secondary mg-b-14">
                    {" "}
                    In addition to their design skills, a web designer also
                    needs to have a good understanding of coding languages such
                    as HTML, CSS, and JavaScript. This allows them to bring
                    their designs to life by coding the website and implementing
                    interactive elements. They may also need to work closely
                    with web developers to ensure that the design is implemented
                    correctly and that any technical issues are resolved.
                    Overall, a web designer plays a crucial role in creating
                    visually appealing and user-friendly websites that
                    effectively communicate the client&#39;s message
                  </p>
                </div>
              </div>
            </CardBody>
          </Card>
        </Col>
      </Row>
    </Col>
  );
};
export default ScrollpyNested;
